using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers.World
{
    public class FactionManager : Singleton<FactionManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, SystemFaction> _systemFactions;
        private List<FactionRelation> _relations;

        public SystemFaction GetFaction(uint id)
        {
            if (_systemFactions.ContainsKey(id))
                return _systemFactions[id];

            return null;
        }

        public void AddFaction(SystemFaction faction) {
            if (!_systemFactions.ContainsKey(faction.Id))
                _systemFactions.Add(faction.Id, faction);
        }

        public void Load()
        {
            _systemFactions = new Dictionary<uint, SystemFaction>();
            _relations = new List<FactionRelation>();
            using (var connection = SQLite.CreateConnection())
            {
                _log.Info("Loading system factions...");
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
                                Id = reader.GetUInt32("id"),
                                Name = LocalizationManager.Instance.Get("system_factions", "name", reader.GetUInt32("id")),
                                OwnerName = reader.GetString("owner_name"),
                                UnitOwnerType = (sbyte) reader.GetInt16("owner_type_id"),
                                OwnerId = reader.GetUInt32("owner_id"),
                                PoliticalSystem = reader.GetByte("political_system_id"),
                                MotherId = reader.GetUInt32("mother_id"),
                                AggroLink = reader.GetBoolean("aggro_link", true),
                                GuardHelp = reader.GetBoolean("guard_help", true),
                                DiplomacyTarget = reader.GetBoolean("is_diplomacy_tgt", true)
                            };
                            _systemFactions.Add(faction.Id, faction);
                        }
                    }
                }

                _log.Info("Loaded {0} system factions", _systemFactions.Count);
                _log.Info("Loading faction relations...");
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
                                Id = reader.GetUInt32("faction1_id"),
                                Id2 = reader.GetUInt32("faction2_id"),
                                State = (RelationState) reader.GetByte("state_id")
                            };
                            _relations.Add(relation);

                            var faction = _systemFactions[relation.Id];
                            faction.Relations.Add(relation.Id2, relation);
                            faction = _systemFactions[relation.Id2];
                            faction.Relations.Add(relation.Id, relation);
                        }
                    }
                }

                _log.Info("Loaded {0} faction relations", _relations.Count);
            }
        }

        public void SendFactions(Character character)
        {
            if (_systemFactions.Values.Count == 0)
                character.SendPacket(new SCFactionListPacket());
            else
            {
                var factions = _systemFactions.Values.ToArray();
                for (var i = 0; i < factions.Length; i += 20)
                {
                    var temp = new SystemFaction[factions.Length - i <= 20 ? factions.Length - i : 20];
                    Array.Copy(factions, i, temp, 0, temp.Length);
                    character.SendPacket(new SCFactionListPacket(temp));
                }
            }
        }

        public void SendRelations(Character character)
        {
            if (_relations.Count == 0)
                character.SendPacket(new SCFactionRelationListPacket());
            else
            {
                var factions = _relations.ToArray();
                for (var i = 0; i < factions.Length; i += 200)
                {
                    var temp = new FactionRelation[factions.Length - i <= 200 ? factions.Length - i : 200];
                    Array.Copy(factions, i, temp, 0, temp.Length);
                    character.SendPacket(new SCFactionRelationListPacket(temp));
                }
            }
        }
    }
}
