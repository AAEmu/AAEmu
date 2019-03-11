using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class SlaveManager : Singleton<SlaveManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, SlaveTemplate> _slaveTemplates;
        private Dictionary<uint, Slave> _activeSlaves;

        private SlaveTemplate GetSlaveTemplate(uint id)
        {
            return _slaveTemplates.ContainsKey(id) ? _slaveTemplates[id] : null;
        }

        private Slave GetActiveSlaveByOwnerObjId(uint objId)
        {
            return _activeSlaves.ContainsKey(objId) ? _activeSlaves[objId] : null;
        }

        private Slave GetActiveSlaveByObjId(uint objId)
        {
            foreach (var slave in _activeSlaves.Values)
            {
                if (slave.ObjId == objId) return slave;
            }

            return null;
        }

        private Slave GetActiveSlaveBytlId(uint tlId)
        {
            foreach (var slave in _activeSlaves.Values)
            {
                if (slave.TlId == tlId) return slave;
            }

            return null;
        }

        public void UnbindSlave(GameConnection connection, uint tlId)
        {
            // TODO
            var unit = connection.ActiveChar;
            var activeSlaveInfo = GetActiveSlaveBytlId(tlId);
            if (activeSlaveInfo == null) return;
            unit.SendPacket(new SCUnitDetachedPacket(unit.ObjId, 5));
        }

        public void BindSlave(GameConnection connection, uint tlId)
        {
            // TODO
            var unit = connection.ActiveChar;
            var activeSlaveInfo = GetActiveSlaveBytlId(tlId);
            if (activeSlaveInfo == null) return;
            unit.SendPacket(new SCUnitAttachedPacket(unit.ObjId, 1, 6, activeSlaveInfo.ObjId));
            unit.BroadcastPacket(new SCTargetChangedPacket(unit.ObjId, activeSlaveInfo.ObjId), true);
            unit.CurrentTarget = activeSlaveInfo;
            unit.SendPacket(new SCSlaveBoundPacket(unit.Id, activeSlaveInfo.ObjId));
        }

        // TODO - GameConnection connection
        public void Delete(Character owner, uint objId)
        {
            var activeSlaveInfo = GetActiveSlaveByObjId(objId);
            if (activeSlaveInfo == null) return;

            owner.SendPacket(new SCSlaveDespawnPacket(objId));
            owner.SendPacket(new SCSlaveRemovedPacket(owner.ObjId, activeSlaveInfo.TlId));
            _activeSlaves.Remove(owner.ObjId);
            activeSlaveInfo.Delete();
        }

        public void Create(Character owner, SkillItem skillData)
        {
            var activeSlaveInfo = GetActiveSlaveByOwnerObjId(owner.ObjId);
            if (activeSlaveInfo != null)
            {
                // TODO - IF TO FAR AWAY DONT DELETE
                Delete(owner, activeSlaveInfo.ObjId);
                return;
            }

            var item = owner.Inventory.GetItem(skillData.ItemId);
            if (item == null) return;

            var itemTemplate = (SummonSlaveTemplate)ItemManager.Instance.GetTemplate(item.TemplateId);
            if (itemTemplate == null) return;

            var slaveTemplate = GetSlaveTemplate(itemTemplate.SlaveId);
            if (slaveTemplate == null) return;

            var tlId = (ushort)TlIdManager.Instance.GetNextId();
            var objId = ObjectIdManager.Instance.GetNextId();

            // TODO
            owner.SendPacket(new SCSlaveCreatedPacket(owner.ObjId, tlId, objId, false, 0, owner.Name));
            var template = new Slave
            {
                TlId = tlId,
                ObjId = objId,
                TemplateId = slaveTemplate.Id,
                Position = owner.Position.Clone(),
                Name = slaveTemplate.Name,
                Level = (byte)slaveTemplate.Level,
                ModelId = slaveTemplate.ModelId,
                Template = slaveTemplate,
                Hp = 100000000,
                Mp = 10000,
                ModelParams = new UnitCustomModelParams(),
                Faction = owner.Faction,
                Id = 10 // TODO
            };
            template.Position.X += 5.0f;
            template.Position.Y += 5.0f;
            template.Spawn();
            owner.SendPacket(new SCSlaveStatePacket(objId, tlId, owner.Name, owner.Id, template.Id));
            // TODO - DOODAD SERVER SIDE
            foreach (var doodadBinding in template.Template.DoodadBindings)
            {
                var doodad = new Doodad
                {
                    ObjId = ObjectIdManager.Instance.GetNextId(),
                    TemplateId = doodadBinding.DoodadId,
                    OwnerObjId = owner.ObjId,
                    ParentObjId = template.ObjId,
                    AttachPoint = (byte)doodadBinding.AttachPointId,
                    Position = new Point(0f, 3.204f, 12588.96f, 0, 0, 0),
                    OwnerId = owner.Id,
                    PlantTime = DateTime.Now,
                    OwnerType = DoodadOwnerType.Slave,
                    DbId = template.Id,
                    Template = DoodadManager.Instance.GetTemplate(doodadBinding.DoodadId),
                    Data = (byte)doodadBinding.AttachPointId
                };
                doodad.SetScale(doodadBinding.Scale);
                doodad.FuncGroupId = doodad.GetGroupId();
                owner.SendPacket(new SCDoodadCreatedPacket(doodad));
            }

            _activeSlaves.Add(owner.ObjId, template);
            owner.SendPacket(new SCMySlavePacket(template.ObjId, template.TlId, template.Name, template.TemplateId, template.Hp, template.Mp,
                template.Position.X, template.Position.Y, template.Position.Z));
        }

        public void Load()
        {
            _slaveTemplates = new Dictionary<uint, SlaveTemplate>();
            _activeSlaves = new Dictionary<uint, Slave>();

            #region SQLLite

            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM slaves";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new SlaveTemplate
                            {
                                Id = reader.GetUInt32("id"),
                                Name = reader.GetString("name"),
                                ModelId = reader.GetUInt32("model_id"),
                                Mountable = reader.GetBoolean("mountable"),
                                SpawnXOffset = reader.GetFloat("spawn_x_offset"),
                                SpawnYOffset = reader.GetFloat("spawn_y_offset"),
                                FactionId = reader.GetUInt32("faction_id", 0),
                                Level = reader.GetUInt32("level"),
                                Cost = reader.GetInt32("cost"),
                                SlaveKindId = reader.GetUInt32("slave_kind_id"),
                                SpawnValidAreaRance = reader.GetUInt32("spawn_valid_area_range", 0),
                                SlaveInitialItemPackId = reader.GetUInt32("slave_initial_item_pack_id", 0),
                                SlaveCustomizingId = reader.GetUInt32("slave_customizing_id", 0),
                                Customizable = reader.GetBoolean("customizable", false)
                            };
                            _slaveTemplates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM slave_initial_buffs";
                    command.Prepare();

                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new SlaveInitialBuffs
                            {
                                Id = reader.GetUInt32("id"),
                                SlaveId = reader.GetUInt32("slave_id"),
                                BuffId = reader.GetUInt32("buff_id")
                            };
                            if (_slaveTemplates.ContainsKey(template.SlaveId))
                            {
                                _slaveTemplates[template.SlaveId].InitialBuffs.Add(template);
                            }
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM slave_passive_buffs";
                    command.Prepare();

                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new SlavePassiveBuffs
                            {
                                Id = reader.GetUInt32("id"),
                                OwnerId = reader.GetUInt32("owner_id"),
                                OwnerType = reader.GetString("owner_type"),
                                PassiveBuffId = reader.GetUInt32("passive_buff_id")
                            };
                            if (_slaveTemplates.ContainsKey(template.OwnerId))
                            {
                                _slaveTemplates[template.OwnerId].PassiveBuffs.Add(template);
                            }
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM slave_doodad_bindings";
                    command.Prepare();

                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new SlaveDoodadBindings
                            {
                                Id = reader.GetUInt32("id"),
                                OwnerId = reader.GetUInt32("owner_id"),
                                OwnerType = reader.GetString("owner_type"),
                                AttachPointId = reader.GetInt32("attach_point_id"),
                                DoodadId = reader.GetUInt32("doodad_id"),
                                Persist = reader.GetBoolean("persist"),
                                Scale = reader.GetFloat("scale")
                            };
                            if (_slaveTemplates.ContainsKey(template.OwnerId))
                            {
                                _slaveTemplates[template.OwnerId].DoodadBindings.Add(template);
                            }
                        }
                    }
                }
            }

            #endregion
        }
    }
}
