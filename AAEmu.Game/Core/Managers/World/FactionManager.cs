using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Менеджер фракций, загружающий данные из таблиц <c>system_factions</c>
/// и <c>system_faction_relations</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class FactionManager(ILocalizationManager localizationManager) : Singleton<FactionManager>, IFactionManager
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

    /// <summary>
    /// Загружает системные фракции и их отношения.
    /// </summary>
    /// <remarks>
    /// Схемы таблиц (проверены по compact.sqlite3):
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <c>system_factions</c>: id (PK), owner_name, owner_type_id, owner_id,
    ///       political_system_id, mother_id, aggro_link, guard_help, is_diplomacy_tgt,
    ///       diplomacy_link_id, icon_path, name
       ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <c>system_faction_relations</c>: id (PK), faction1_id, faction2_id, state_id
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    public void Load()
    {
        if (_loaded)
            return;

        _systemFactions = [];
        _relations = [];
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
                            Name = localizationManager.Get("system_factions", "name", reader.GetUInt32("id")),
                            DbName = reader.GetString("name"),
                            OwnerName = reader.GetString("owner_name"),
                            UnitOwnerType = (sbyte)reader.GetInt16("owner_type_id"),
                            OwnerId = reader.GetUInt32("owner_id"),
                            PoliticalSystem = reader.GetByte("political_system_id"),
                            MotherId = (FactionsEnum)reader.GetUInt32("mother_id"),
                            AggroLink = reader.GetBoolean("aggro_link", true),
                            GuardHelp = reader.GetBoolean("guard_help", true),
                            DiplomacyTarget = reader.GetBoolean("is_diplomacy_tgt", true),
                            DiplomacyLinkId = reader.GetUInt32("diplomacy_link_id"),
                            IconPath = reader.GetString("icon_path")
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
                            Id = reader.GetUInt32("id"),
                            Faction1Id = (FactionsEnum)reader.GetUInt32("faction1_id"),
                            Faction2Id = (FactionsEnum)reader.GetUInt32("faction2_id"),
                            State = (RelationState)reader.GetByte("state_id")
                        };

                        _relations.Add(relation);

                        var faction1 = _systemFactions[relation.Faction1Id];
                        var faction2 = _systemFactions[relation.Faction2Id];
                        faction1.Relations.TryAdd(relation.Faction2Id, relation);
                        faction2.Relations.TryAdd(relation.Faction1Id, relation);
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
