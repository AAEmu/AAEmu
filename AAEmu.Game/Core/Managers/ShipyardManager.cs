using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class ShipyardManager : Singleton<ShipyardManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, ShipyardsTemplate> _shipyards;
        private Dictionary<uint, ShipyardSteps> _shipyardSteps;
        private Dictionary<uint, ShipyardRewards> _shipyardRewards;

        public ShipyardSteps GetShipyardSteps(uint shipyardId)
        {
            foreach (var value in _shipyardSteps.Values)
            {
                if (value.ShipyardId == shipyardId) return value;
            }

            return null;
        }

        public void Load()
        {
            _shipyards = new Dictionary<uint, ShipyardsTemplate>();
            _shipyardSteps = new Dictionary<uint, ShipyardSteps>();
            _shipyardRewards = new Dictionary<uint, ShipyardRewards>();

            _log.Info("Loading Shipyards...");
            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM shipyards";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new ShipyardsTemplate
                            {
                                Id = reader.GetUInt32("id"),
                                Name = reader.GetString("name"),
                                MainModelId = reader.GetUInt32("main_model_id"),
                                ItemId = reader.GetUInt32("item_id"),
                                SpawnOffsetFront = reader.GetFloat("spawn_offset_front"),
                                SpawnOffsetZ = reader.GetFloat("spawn_offset_z"),
                                BuildRadius = reader.GetInt32("build_radius"),
                                TaxDuration = reader.GetInt32("tax_duration", 0),
                                OriginItemId = reader.GetUInt32("origin_item_id", 0),
                                TaxationId = reader.GetInt32("taxation_id")
                            };
                            _shipyards.Add(template.Id, template);
                        }
                    }
                }
                _log.Info("Loaded {0} shipyards", _shipyards.Count);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM shipyard_steps";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new ShipyardSteps()
                            {
                                Id = reader.GetUInt32("id"),
                                ShipyardId = reader.GetUInt32("shipyard_id"),
                                Step = reader.GetInt32("step"),
                                ModelId = reader.GetUInt32("model_id"),
                                SkillId = reader.GetUInt32("skill_id"),
                                NumActions = reader.GetInt32("num_actions"),
                                MaxHp = reader.GetInt32("max_hp")
                            };
                            _shipyardSteps.Add(template.Id, template);
                        }
                    }
                }
                _log.Info("Loaded {0} shipyard steps", _shipyardSteps.Count);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM shipyard_rewards";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new ShipyardRewards()
                            {
                                Id = reader.GetUInt32("id"),
                                ShipyardId = reader.GetUInt32("shipyard_id"),
                                DoodadId = reader.GetUInt32("doodad_id"),
                                OnWater = reader.GetBoolean("on_water"),
                                Radius = reader.GetInt32("radius"),
                                Count = reader.GetInt32("count")
                            };
                            _shipyardRewards.Add(template.Id, template);
                        }
                    }
                }
                _log.Info("Loaded {0} shipyard rewards", _shipyardRewards.Count);
            }
        }
    }
}
