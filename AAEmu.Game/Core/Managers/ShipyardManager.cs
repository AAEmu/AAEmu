using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class ShipyardManager : Singleton<ShipyardManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, ShipyardsTemplate> _shipyards;
        private Dictionary<ulong, Shipyard> _allShipyard;

        public void Create(Character owner, ShipyardData shipyardData, int step)
        {
            var pos = owner.Transform.CloneAsSpawnPosition();
            pos.X = shipyardData.X;
            pos.Y = shipyardData.Y;
            pos.Z = shipyardData.Z;
            pos.Yaw = (float)(MathUtil.ConvertDirectionToRadian(Helpers.ConvertRotation((short)shipyardData.zRot)));
            var objId = ObjectIdManager.Instance.GetNextId();
            var template = _shipyards[shipyardData.TemplateId];
            //var shipId = 3039u;

            var shipyard = new Shipyard
            {
                ObjId = objId,
                Faction = owner.Faction,
                Level = 30,
                Hp = 10000,
                MaxHp = 10000,
                Name = owner.Name,
                ModelId = template.ShipyardSteps[step].ModelId
            };
            shipyard.Transform.ApplyWorldSpawnPosition(pos);

            shipyard.Template = new ShipyardData
            {
                Id = shipyardData.Id,
                TemplateId = template.Id,
                X = pos.X,
                Y = pos.Y,
                Z = pos.Z,
                zRot = pos.Yaw,
                MoneyAmount = 0,
                Actions = step,
                Type = template.OriginItemId, // type1
                OwnerName = owner.Name,
                Type2 = owner.Id, // type2
                Type3 = owner.Faction.Id, // type3
                Spawned = DateTime.MinValue,
                ObjId = objId,
                Hp = template.ShipyardSteps[step].MaxHp * 100,
                Step = step
            };
            shipyard.Buffs.AddBuff(new Buff(shipyard, shipyard, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(3554), null, System.DateTime.UtcNow));
            shipyard.Spawn();
        }

        public void Load()
        {
            _shipyards = new Dictionary<uint, ShipyardsTemplate>();
            _allShipyard = new Dictionary<ulong, Shipyard>();

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
                            if (_shipyards.ContainsKey(template.ShipyardId))
                            {
                                _shipyards[template.ShipyardId].ShipyardSteps.Add(template.Step, template);
                            }
                        }
                    }
                }
            }
        }
    }
}
