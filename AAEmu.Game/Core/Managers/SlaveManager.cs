using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Commons.IO;
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
using AAEmu.Game.Models.Tasks.Slave;
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
        private Dictionary<uint, Slave> _tlSlaves;
        public Dictionary<uint, Dictionary<int, Point>> _attachPoints;

        private SlaveTemplate GetSlaveTemplate(uint id)
        {
            return _slaveTemplates.ContainsKey(id) ? _slaveTemplates[id] : null;
        }

        public Slave GetActiveSlaveByOwnerObjId(uint objId)
        {
            return _activeSlaves.ContainsKey(objId) ? _activeSlaves[objId] : null;
        }
        
        public IEnumerable<Slave> GetActiveSlavesByKind(SlaveKind kind)
        {
            return _activeSlaves.Select(i => i.Value).Where(s => s.Template.SlaveKind == kind);
        }

        public IEnumerable<Slave> GetActiveSlavesByKinds(SlaveKind[] kinds)
        {
            return _activeSlaves.Where(s => kinds.Contains(s.Value.Template.SlaveKind)).Select(s => s.Value);
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
            var slave = _tlSlaves[tlId];
            unit.BroadcastPacket(new SCUnitDetachedPacket(unit.ObjId, 5), true);
            slave.Bounded = null;
        }
        
        public void BindSlave(Character character, uint objId, byte attachPointId, byte bondKind)
        {
            var slave = GetActiveSlaveByObjId(objId);
            character.BroadcastPacket(new SCUnitAttachedPacket(character.ObjId, attachPointId, bondKind, objId), true);
            character.BroadcastPacket(new SCSlaveBoundPacket(character.Id, objId), true);
            slave.Bounded = character;
        }

        public void BindSlave(GameConnection connection, uint tlId)
        {
            var unit = connection.ActiveChar;
            var slave = _tlSlaves[tlId];
            
            unit.BroadcastPacket(new SCUnitAttachedPacket(unit.ObjId, 1, 6, slave.ObjId), true);
            unit.BroadcastPacket(new SCTargetChangedPacket(unit.ObjId, slave.ObjId), true);
            unit.CurrentTarget = slave;
            unit.BroadcastPacket(new SCSlaveBoundPacket(unit.Id, slave.ObjId), true);
            slave.Bounded = unit;
        }

        // TODO - GameConnection connection
        public void Delete(Character owner, uint objId)
        {
            var activeSlaveInfo = GetActiveSlaveByObjId(objId);
            if (activeSlaveInfo == null) return;

            foreach (var doodad in activeSlaveInfo.AttachedDoodads)
            {
                doodad.Delete();
            }

            foreach (var attachedSlave in activeSlaveInfo.AttachedSlaves)
            {
                _tlSlaves.Remove(attachedSlave.TlId);
                attachedSlave.Delete();
            }

            BoatPhysicsManager.Instance.RemoveShip(activeSlaveInfo);
            owner.BroadcastPacket(new SCSlaveDespawnPacket(objId), true);
            owner.BroadcastPacket(new SCSlaveRemovedPacket(owner.ObjId, activeSlaveInfo.TlId), true);
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

            var item = owner.Inventory.GetItemById(skillData.ItemId);
            if (item == null) return;

            var itemTemplate = (SummonSlaveTemplate)ItemManager.Instance.GetTemplate(item.TemplateId);
            if (itemTemplate == null) return;

            var slaveTemplate = GetSlaveTemplate(itemTemplate.SlaveId);
            if (slaveTemplate == null) return;

            var tlId = (ushort)TlIdManager.Instance.GetNextId();
            var objId = ObjectIdManager.Instance.GetNextId();

            var spawnPos = owner.Position.Clone();
            spawnPos.X += slaveTemplate.SpawnXOffset;
            spawnPos.Y += slaveTemplate.SpawnYOffset;
            if (slaveTemplate.SlaveKind == SlaveKind.Boat)
                spawnPos.Z = 100.0f;

            // TODO
            owner.BroadcastPacket(new SCSlaveCreatedPacket(owner.ObjId, tlId, objId, false, 0, owner.Name), true);
            var template = new Slave
            {
                TlId = tlId,
                ObjId = objId,
                TemplateId = slaveTemplate.Id,
                Position = spawnPos,
                Name = slaveTemplate.Name,
                Level = (byte)slaveTemplate.Level,
                ModelId = slaveTemplate.ModelId,
                Template = slaveTemplate,
                Hp = 100000000,
                Mp = 10000,
                ModelParams = new UnitCustomModelParams(),
                Faction = owner.Faction,
                Id = 10, // TODO
                Summoner = owner,
                AttachedDoodads = new List<Doodad>(),
                AttachedSlaves = new List<Slave>(),
                SpawnTime = DateTime.Now
            };
            template.Spawn();
            
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
                    OwnerId = owner.Id,
                    PlantTime = DateTime.Now,
                    OwnerType = DoodadOwnerType.Slave,
                    DbHouseId = template.Id,
                    Template = DoodadManager.Instance.GetTemplate(doodadBinding.DoodadId),
                    Data = (byte)doodadBinding.AttachPointId,
                    ParentObj = template
                };

                doodad.SetScale(doodadBinding.Scale);

                doodad.CurrentPhaseId = doodad.GetFuncGroupId();

                if (_attachPoints.ContainsKey(template.ModelId))
                {
                    if (_attachPoints[template.ModelId].ContainsKey(doodadBinding.AttachPointId))
                    {
                        doodad.AttachPosition = _attachPoints[template.ModelId][doodadBinding.AttachPointId];
                        doodad.Position = template.Position.Clone();
                    }
                    else
                    {
                        _log.Warn("Model id: {0} incomplete attach point information");
                    }                    
                }
                else
                {
                    doodad.Position = new Point(0f, 3.204f, 12588.96f, 0, 0, 0);
                    _log.Warn("Model id: {0} has no attach point information");
                }

                template.AttachedDoodads.Add(doodad);

                doodad.Spawn();
            }
            
            foreach (var slaveBinding in template.Template.SlaveBindings)
            {
                var childSlaveTemplate = GetSlaveTemplate(slaveBinding.SlaveId);
                var ctlId = (ushort)TlIdManager.Instance.GetNextId();
                var cobjId = ObjectIdManager.Instance.GetNextId();
                var childSlave = new Slave()
                {
                    TlId = ctlId,
                    ObjId = cobjId,
                    TemplateId = childSlaveTemplate.Id,
                    Position = spawnPos,
                    Name = childSlaveTemplate.Name,
                    Level = (byte)childSlaveTemplate.Level,
                    ModelId = childSlaveTemplate.ModelId,
                    Template = childSlaveTemplate,
                    Hp = 100000000,
                    Mp = 10000,
                    ModelParams = new UnitCustomModelParams(),
                    Faction = owner.Faction,
                    Id = 11, // TODO
                    Summoner = owner,
                    AttachedDoodads = new List<Doodad>(),
                    AttachedSlaves = new List<Slave>(),
                    SpawnTime = DateTime.Now,
                    AttachPointId = (sbyte) slaveBinding.AttachPointId,
                    OwnerObjId = template.ObjId
                };
                
                if (_attachPoints.ContainsKey(template.ModelId))
                {
                    if (_attachPoints[template.ModelId].ContainsKey((int) slaveBinding.AttachPointId))
                    {
                        var attachPoint = _attachPoints[template.ModelId][(int) slaveBinding.AttachPointId];
                        // childSlave.AttachPosition = _attachPoints[template.ModelId][(int) slaveBinding.AttachPointId];
                        childSlave.Position = template.Position.Clone();
                        childSlave.Position.X += attachPoint.X;
                        childSlave.Position.Y += attachPoint.Y;
                        childSlave.Position.Z += attachPoint.Z;
                    }
                    else
                    {
                        childSlave.Position = template.Position.Clone();
                        _log.Warn("Model id: {0} incomplete attach point information");
                    }                    
                }
                
                template.AttachedSlaves.Add(childSlave);
                _tlSlaves.Add(childSlave.TlId, childSlave);
                childSlave.Spawn();
            }

            _tlSlaves.Add(template.TlId, template);
            _activeSlaves.Add(owner.ObjId, template);
            
            if (new[] {SlaveKind.BigSailingShip, SlaveKind.Boat, SlaveKind.Fishboat, SlaveKind.SmallSailingShip, SlaveKind.MerchantShip, SlaveKind.Speedboat}.Contains(template.Template.SlaveKind))
                BoatPhysicsManager.Instance.AddShip(template);
            
            owner.SendPacket(new SCMySlavePacket(template.ObjId, template.TlId, template.Name, template.TemplateId, template.Hp, template.Mp,
                template.Position.X, template.Position.Y, template.Position.Z));
        }

        public void Load()
        {
            _slaveTemplates = new Dictionary<uint, SlaveTemplate>();
            _activeSlaves = new Dictionary<uint, Slave>();
            _tlSlaves = new Dictionary<uint, Slave>();

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
                                Name = LocalizationManager.Instance.Get("slaves", "name", reader.GetUInt32("id"), reader.GetString("name")),
                                ModelId = reader.GetUInt32("model_id"),
                                Mountable = reader.GetBoolean("mountable", true),
                                SpawnXOffset = reader.GetFloat("spawn_x_offset"),
                                SpawnYOffset = reader.GetFloat("spawn_y_offset"),
                                FactionId = reader.GetUInt32("faction_id", 0),
                                Level = reader.GetUInt32("level"),
                                Cost = reader.GetInt32("cost"),
                                SlaveKind = (SlaveKind)reader.GetUInt32("slave_kind_id"),
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
                                Persist = reader.GetBoolean("persist", true),
                                Scale = reader.GetFloat("scale")
                            };
                            if (_slaveTemplates.ContainsKey(template.OwnerId))
                            {
                                _slaveTemplates[template.OwnerId].DoodadBindings.Add(template);
                            }
                        }
                    }
                }
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM slave_bindings";
                    command.Prepare();

                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new SlaveBindings()
                            {
                                Id = reader.GetUInt32("id"),
                                OwnerId = reader.GetUInt32("owner_id"),
                                OwnerType = reader.GetString("owner_type"),
                                AttachPointId = reader.GetUInt32("attach_point_id"),
                                SlaveId = reader.GetUInt32("slave_id")
                            };
                            
                            if (_slaveTemplates.ContainsKey(template.OwnerId))
                            {
                                _slaveTemplates[template.OwnerId].SlaveBindings.Add(template);
                            }
                        }
                    }
                }
            }

            #endregion


            _log.Info("Loading Slave Model Attach Points...");

            var contents = FileManager.GetFileContents($"{FileManager.AppPath}Data/slave_attach_points.json");
            if (string.IsNullOrWhiteSpace(contents))
                throw new IOException(
                    $"File {FileManager.AppPath}Data/slave_attach_points.json doesn't exists or is empty.");

            List<SlaveModelAttachPoint> attachPoints;
            if (JsonHelper.TryDeserializeObject(contents, out attachPoints, out _))
                _log.Info("Slave model attach points loaded...");
            else
                _log.Warn("Slave model attach points not loaded...");
            
            _attachPoints = new Dictionary<uint, Dictionary<int, Point>>();
            foreach (var set in attachPoints)
            {
                _attachPoints[set.ModelId] = set.AttachPoints;
            }
        }
        
        public void Initialize()
        {
            var sendMySlaveTask = new SendMySlaveTask();
            TaskManager.Instance.Schedule(sendMySlaveTask, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }

        public void SendMySlavePacketToAllOwners() {
            foreach (var (ownerObjId, slave) in _activeSlaves)
            {
                var owner = WorldManager.Instance.GetCharacterByObjId(ownerObjId);
                owner?.SendPacket(new SCMySlavePacket(slave.ObjId, slave.TlId, slave.Name, slave.TemplateId, slave.Hp, slave.Mp,
                    slave.Position.X, slave.Position.Y, slave.Position.Z));
            }
        }
    }

    public class SlaveModelAttachPoint
    {
        public uint ModelId;
        public Dictionary<int, Point> AttachPoints;
    }
}
