using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Tasks.Shipyard;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class ShipyardManager : Singleton<ShipyardManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public Dictionary<uint, ShipyardsTemplate> _shipyards;
        private Dictionary<uint, Shipyard> _allShipyard;
        private List<uint> _removedShipyards;

        public void Initialize()
        {
            _shipyards = new Dictionary<uint, ShipyardsTemplate>();
            _allShipyard = new Dictionary<uint, Shipyard>();
            _removedShipyards = new List<uint>();
            _log.Info("Initialising Shipyard Manager...");
            ShipyardTickStart();
        }

        private void ShipyardTickStart()
        {
            _log.Warn("ShipyardDecayTickStart: Started");

            var shipyardTickStartTask = new ShipyardTickTask();
            TaskManager.Instance.Schedule(shipyardTickStartTask, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        public Shipyard Create(Character owner, ShipyardData shipyardData)
        {
            if (!_shipyards.ContainsKey(shipyardData.TemplateId))
                return null;

            var pos = owner.Transform.CloneAsSpawnPosition();
            pos.X = shipyardData.X;
            pos.Y = shipyardData.Y;
            pos.Z = shipyardData.Z;
            pos.Yaw = shipyardData.zRot;

            var objId = ObjectIdManager.Instance.GetNextId();
            var shipId = ShipyardIdManager.Instance.GetNextId();

            var template = _shipyards[shipyardData.TemplateId];

            var shipyard = new Shipyard();
            shipyard.ObjId = objId;
            shipyard.Template = template;
            shipyard.Faction = owner.Faction;
            shipyard.Level = 30;
            shipyard.Hp = shipyard.MaxHp;
            shipyard.Name = owner.Name;
            shipyard.ModelId = template.ShipyardSteps[shipyardData.Step].ModelId;
            shipyard.Transform.ApplyWorldSpawnPosition(pos);

            shipyard.ShipyardData = new ShipyardData();
            shipyard.ShipyardData.Id = shipId;
            shipyard.ShipyardData.TemplateId = template.Id;
            shipyard.ShipyardData.X = pos.X;
            shipyard.ShipyardData.Y = pos.Y;
            shipyard.ShipyardData.Z = pos.Z;
            shipyard.ShipyardData.zRot = pos.Yaw;
            shipyard.ShipyardData.MoneyAmount = 0;
            shipyard.ShipyardData.Actions = shipyardData.Step;
            shipyard.ShipyardData.Type = template.OriginItemId;
            shipyard.ShipyardData.OwnerName = owner.Name;
            shipyard.ShipyardData.Type2 = owner.Id;
            shipyard.ShipyardData.Type3 = owner.Faction.Id;
            shipyard.ShipyardData.Spawned = DateTime.Now;
            shipyard.ShipyardData.ObjId = objId;
            shipyard.ShipyardData.Hp = template.ShipyardSteps[shipyardData.Step].MaxHp * 100;
            shipyard.ShipyardData.Step = shipyardData.Step;
            _allShipyard.Add(shipId, shipyard);
            shipyard.Spawn();

            return shipyard;
        }

        public void RemoveShipyard(Shipyard shipyard)
        {
            var shipId = (uint)shipyard.ShipyardData.Id;
            // Remove Shipyard from Shipyard tables
            _removedShipyards.Add(shipId);
            _allShipyard.Remove(shipId);
            ShipyardIdManager.Instance.ReleaseId(shipId);
            ObjectIdManager.Instance.ReleaseId(shipyard.ObjId);
            shipyard.Delete();
        }

        public void ShipyardCompleted(Shipyard shipyard)
        {
            var character = WorldManager.Instance.GetCharacter(shipyard.ShipyardData.OwnerName);
            var found = character.Inventory.Bag.GetAllItemsByTemplate(shipyard.Template.ItemId, -1, out var foundItems, out _);
            if (found)
            {
                // calculate skillData
                var skillData = (SkillItem)SkillCaster.GetByType(SkillCasterType.Item);
                skillData.ItemId = foundItems[0].Id;
                SlaveManager.Instance.Create(character, skillData, true, shipyard);
            }
            RemoveShipyard(shipyard);
        }

        public void ShipyardCompletedTask(Shipyard shipyard)
        {
            var character = WorldManager.Instance.GetCharacter(shipyard.ShipyardData.OwnerName);
            character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Shipyard, shipyard.Template.ItemId, 1, 0);
            var shipyardCompleteTask = new ShipyardCompleteTask();
            shipyardCompleteTask._shipyard = shipyard;

            shipyard.ShipyardData.Step = 1000; // last step, the ceremony of launching the ship
            character.BroadcastPacket(new SCShipyardStatePacket(shipyard.ShipyardData), true);

            var animTime = shipyard.Template.CeremonyAnimTime;
            TaskManager.Instance.Schedule(shipyardCompleteTask, TimeSpan.FromMilliseconds(animTime));
        }

        public void ShipyardTick()
        {
            foreach (var shipyard in _allShipyard)
            {
                UpdateShipyardInfo(shipyard.Value);
            }
        }

        private void UpdateShipyardInfo(Shipyard shipyard)
        {
            var isDecaying = (DateTime.Now >= shipyard.ShipyardData.Spawned.AddDays(3));

            SetProtectionBuff(shipyard, isDecaying);
            SetDecayBuff(shipyard, isDecaying);
        }

        private void SetProtectionBuff(Shipyard shipyard, bool isDecay)
        {
            if (!isDecay)
            {
                var duration = shipyard.ShipyardData.Spawned - DateTime.Now;
                var mins = Math.Round(duration.TotalMinutes) * 60000;

                var timeleft = shipyard.Template.TaxDuration + mins;

                if (shipyard.Buffs.CheckBuff((uint)BuffConstants.BuffTaxprotection))
                    return;

                var protectionBuffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.BuffTaxprotection);
                if (protectionBuffTemplate != null)
                {
                    var casterObj = new SkillCasterUnit(shipyard.ObjId);
                    shipyard.Buffs.AddBuff(new Buff(shipyard, shipyard, casterObj, protectionBuffTemplate, null, DateTime.Now), 0, (int)timeleft);
                }
                else
                {
                    _log.Error("Unable to find Protection Buff template");
                }
            }
            else
            {
                if (shipyard.Buffs.CheckBuff((uint)BuffConstants.BuffTaxprotection))
                    shipyard.Buffs.RemoveBuff((uint)BuffConstants.BuffTaxprotection);
            }
        }

        private void SetDecayBuff(Shipyard shipyard, bool isDecay)
        {
            if (isDecay)
            {
                if (shipyard.Buffs.CheckBuff((uint)BuffConstants.BuffDeterioration))
                {
                    shipyard.ReduceCurrentHp(shipyard, 7);
                    var character = WorldManager.Instance.GetCharacter(shipyard.ShipyardData.OwnerName);
                    character.SendPacket(new SCUnitStatePacket(shipyard));
                    character.SendPacket(new SCShipyardStatePacket(shipyard.ShipyardData));

                    return;
                }

                var protectionBuffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.BuffDeterioration);
                if (protectionBuffTemplate != null)
                {
                    var casterObj = new SkillCasterUnit(shipyard.ObjId);
                    shipyard.Buffs.AddBuff(new Buff(shipyard, shipyard, casterObj, protectionBuffTemplate, null, DateTime.Now));
                }
                else
                {
                    _log.Error("Unable to find Deterioration Debuff template");
                }
            }
            else
            {
                if (shipyard.Buffs.CheckBuff((uint)BuffConstants.BuffDeterioration))
                    shipyard.Buffs.RemoveBuff((uint)BuffConstants.BuffDeterioration);
            }
        }

        public void Load()
        {
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
                                CeremonyAnimTime = reader.GetInt32("ceremony_anim_time"),
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
