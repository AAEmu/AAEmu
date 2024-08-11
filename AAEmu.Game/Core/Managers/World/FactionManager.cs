using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class FactionManager : Singleton<FactionManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded = false;

    private Dictionary<FactionsEnum, SystemFaction> _systemFactions;
    private List<FactionRelation> _relations;

    public SystemFaction GetFaction(FactionsEnum id)
    {
        return _systemFactions.GetValueOrDefault(id);
    }

    public void AddFaction(SystemFaction faction)
    {
        _systemFactions.TryAdd(faction.Id, faction);
    }

    public void Load()
    {
        if (_loaded)
            return;

        _systemFactions = new();
        _relations = new();
        using (var connection = SQLite.CreateConnection())
        {
            Logger.Info("Loading system factions...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM system_factions";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var faction = new SystemFaction
                        {
                            Id = (FactionsEnum)reader.GetUInt32("id"),
                            Name = LocalizationManager.Instance.Get("system_factions", "name", reader.GetUInt32("id")),
                            OwnerName = reader.GetString("owner_name"),
                            UnitOwnerType = (sbyte)reader.GetInt16("owner_type_id"),
                            OwnerId = reader.GetUInt32("owner_id"),
                            PoliticalSystem = reader.GetByte("political_system_id"),
                            MotherId = (FactionsEnum)reader.GetUInt32("mother_id"),
                            AggroLink = reader.GetBoolean("aggro_link", true),
                            GuardHelp = reader.GetBoolean("guard_help", true),
                            DiplomacyTarget = reader.GetBoolean("is_diplomacy_tgt", true)
                        };
                        _systemFactions.Add(faction.Id, faction);
                    }
                }
            }

            Logger.Info($"Loaded {_systemFactions.Count} system factions");
            Logger.Info("Loading faction relations...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM system_faction_relations";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var relation = new FactionRelation
                        {
                            Id = (FactionsEnum)reader.GetUInt32("faction1_id"),
                            Id2 = (FactionsEnum)reader.GetUInt32("faction2_id"),
                            State = (RelationState)reader.GetByte("state_id")
                        };
                        _relations.Add(relation);

                        var faction = _systemFactions[relation.Id];
                        if (faction.Relations.TryAdd(relation.Id2, relation)) // TODO проверить правильность удаления дублей
                        {
                            faction = _systemFactions[relation.Id2];
                            faction.Relations.Add(relation.Id, relation);
                        }
                    }
                }
            }

            Logger.Info("Loaded {0} faction relations", _relations.Count);
        }

        _loaded = true;
    }

    public void SendFactions(Character character)
    {
        if (_systemFactions.Values.Count == 0)
            character.SendPacket(new SCSystemFactionListPacket());
        else
        {
            var factions = _systemFactions.Values.ToArray();
            var dividedArrays = Helpers.SplitArray(factions, 20); // Разделяем массив на массивы по 20 значений
            foreach (var systemFaction in dividedArrays)
                character.SendPacket(new SCSystemFactionListPacket(systemFaction));
        }
    }

    public void SendRelations(Character character)
    {
        if (_relations.Count == 0)
            character.SendPacket(new SCFactionRelationListPacket());
        else
        {
            var factions = _relations.ToArray();
            var dividedArrays = Helpers.SplitArray(factions, 200); // Разделяем массив на массивы по 200 значений
            foreach (var fr in dividedArrays)
                character.SendPacket(new SCFactionRelationListPacket(fr));
        }
    }
}
