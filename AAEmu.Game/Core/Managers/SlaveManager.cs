using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Slave;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using NLog;
using System.Numerics;
using System.Security.Claims;
using AAEmu.Game.Core.Managers.AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Core.Managers
{
    public class SlaveManager : Singleton<SlaveManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, SlaveTemplate> _slaveTemplates;
        private Dictionary<uint, Slave> _activeSlaves;
        private Dictionary<uint, Slave> _tlSlaves;
        public Dictionary<uint, Dictionary<AttachPointKind, WorldSpawnPosition>> _attachPoints;
        public Dictionary<uint, List<SlaveInitialItems>> _slaveInitialItems; // PackId and List<Slot/ItemData>

        public SlaveTemplate GetSlaveTemplate(uint id)
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

        public Slave GetActiveSlaveByObjId(uint objId)
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

        public void UnbindSlave(Character character, uint tlId, AttachUnitReason reason)
        {
            var slave = _tlSlaves[tlId];
            var attachPoint = slave.AttachedCharacters.FirstOrDefault(x => x.Value == character).Key;
            if (attachPoint != default)
            {
                slave.AttachedCharacters.Remove(attachPoint);
                character.Transform.Parent = null;
                character.Transform.StickyParent = null;
            }
            character.BroadcastPacket(new SCUnitDetachedPacket(character.ObjId, reason), true);
        }

        public void BindSlave(Character character, uint objId, AttachPointKind attachPoint, AttachUnitReason bondKind)
        {
            // Check if the target spot is already taken
            var slave = _tlSlaves.FirstOrDefault(x => x.Value.ObjId == objId).Value;
            //var slave = GetActiveSlaveByObjId(objId);
            if ((slave == null) || (slave.AttachedCharacters.ContainsKey(attachPoint)))
                return;

            character.BroadcastPacket(new SCUnitAttachedPacket(character.ObjId, attachPoint, bondKind, objId), true);
            switch (attachPoint)
            {
                case AttachPointKind.Driver:
                    character.BroadcastPacket(new SCSlaveBoundPacket(character.Id, objId), true);
                    break;
            }

            slave.AttachedCharacters.Add(attachPoint, character);
            character.Transform.Parent = slave.Transform;
            // TODO: move to attach point's position
            character.Transform.Local.SetPosition(0, 0, 0, 0, 0, 0);
        }

        public void BindSlave(GameConnection connection, uint tlId)
        {
            var unit = connection.ActiveChar;
            var slave = _tlSlaves[tlId];
            
            BindSlave(unit,slave.ObjId,AttachPointKind.Driver,AttachUnitReason.NewMaster);
        }

        // TODO - GameConnection connection
        public void Delete(Character owner, uint objId)
        {
            var activeSlaveInfo = GetActiveSlaveByObjId(objId);
            if (activeSlaveInfo == null) return;

            foreach (var character in activeSlaveInfo.AttachedCharacters.Values.ToList())
                UnbindSlave(character, activeSlaveInfo.TlId, AttachUnitReason.SlaveBinding);

            var despawnDelayedTime = DateTime.Now.AddSeconds(activeSlaveInfo.Template.PortalTime - 0.5f); 
            
            activeSlaveInfo.Transform.DetachAll();

            foreach (var doodad in activeSlaveInfo.AttachedDoodads)
            {
                doodad.Despawn = despawnDelayedTime;
                SpawnManager.Instance.AddDespawn(doodad);
                // doodad.Delete();
            }

            foreach (var attachedSlave in activeSlaveInfo.AttachedSlaves)
            {
                _tlSlaves.Remove(attachedSlave.TlId);
                attachedSlave.Despawn = despawnDelayedTime;
                SpawnManager.Instance.AddDespawn(attachedSlave);
                //attachedSlave.Delete();
            }

            BoatPhysicsManager.Instance.RemoveShip(activeSlaveInfo);
            owner.BroadcastPacket(new SCSlaveDespawnPacket(objId), true);
            owner.BroadcastPacket(new SCSlaveRemovedPacket(owner.ObjId, activeSlaveInfo.TlId), true);
            _activeSlaves.Remove(owner.ObjId);

            activeSlaveInfo.Despawn = DateTime.Now.AddSeconds(activeSlaveInfo.Template.PortalTime + 0.5f);
            SpawnManager.Instance.AddDespawn(activeSlaveInfo);
        }

        public void Create(Character owner, SkillItem skillData)
        {
            var activeSlaveInfo = GetActiveSlaveByOwnerObjId(owner.ObjId);
            if (activeSlaveInfo != null)
            {
                // TODO: If too far away, don't delete
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

            var spawnPos = owner.Transform.CloneDetached();
            // owner.SendMessage("SlaveSpawnOffset: x:{0} y:{1}", slaveTemplate.SpawnXOffset, slaveTemplate.SpawnYOffset);
            spawnPos.Local.AddDistanceToFront(Math.Clamp(slaveTemplate.SpawnYOffset, 5f, 50f));
            // INFO: Seems like X offset is defined as the size of the vehicle summoned, but visually it's nicer if we just ignore this 
            // spawnPos.Local.AddDistanceToRight(slaveTemplate.SpawnXOffset);
            if (slaveTemplate.IsABoat())
            {
                // If we're spawning a boat, put it at the water level regardless of our own height
                // TODO: if not at ocean level, get actual target location water body height (for example rivers)
                var worldWaterLevel = WorldManager.Instance.GetWorld(spawnPos.WorldId)?.OceanLevel ?? 100f;
                spawnPos.Local.SetHeight(worldWaterLevel);

                // temporary grab ship information so that we can use it to find a suitable spot in front to summon it
                var tempShipModel = ModelManager.Instance.GetShipModel(slaveTemplate.ModelId);
                var minDepth = tempShipModel.MassBoxSizeZ - tempShipModel.MassCenterZ + 1f;
                for (var inFront = 0f; inFront < (50f + tempShipModel.MassBoxSizeX); inFront += 1f)
                {
                    var depthCheckPos = spawnPos.CloneDetached();
                    depthCheckPos.Local.AddDistanceToFront(inFront);
                    var h = WorldManager.Instance.GetHeight(depthCheckPos);
                    if (h > 0f)
                    {
                        var d = worldWaterLevel - h;
                        if (d > minDepth)
                        {
                            // owner.SendMessage("Extra inFront = {0}, required Depth = {1}", inFront, minDepth);
                            spawnPos = depthCheckPos.CloneDetached();
                            break;
                        }
                    }
                }
            }
            else
            {
                // If a land vehicle, put it a the ground level of it's target spawn location
                // TODO: check for maximum height difference for summoning
                var h = WorldManager.Instance.GetHeight(spawnPos);
                if (h > 0f)
                    spawnPos.Local.SetHeight(h);
            }

            spawnPos.Local.SetRotation(0f, 0f,
                owner.Transform.World.Rotation.Z + (MathF.PI / 2)); // Always spawn horizontal and 90° CCW

            // TODO
            owner.BroadcastPacket(new SCSlaveCreatedPacket(owner.ObjId, tlId, objId, false, 0, owner.Name), true);
            var template = new Slave
            {
                TlId = tlId,
                ObjId = objId,
                TemplateId = slaveTemplate.Id,
                Name = slaveTemplate.Name,
                Level = (byte)slaveTemplate.Level,
                ModelId = slaveTemplate.ModelId,
                Template = slaveTemplate,
                Hp = 1,
                Mp = 1,
                ModelParams = new UnitCustomModelParams(),
                Faction = owner.Faction,
                Id = 10, // TODO
                Summoner = owner,
                SpawnTime = DateTime.Now
            };
            
            if (_slaveInitialItems.TryGetValue(template.Template.SlaveInitialItemPackId, out var itemPack))
            {
                foreach (var initialItem in itemPack)
                {
                    // var newItem = new Item(WorldManager.DefaultWorldId,ItemManager.Instance.GetTemplate(initialItem.itemId),1);
                    var newItem = ItemManager.Instance.Create(initialItem.itemId, 1, 0, false);
                    template.Equipment.AddOrMoveExistingItem(ItemTaskType.Invalid, newItem, initialItem.equipSlotId);
                }
            }

            foreach (var buff in template.Template.InitialBuffs)
                template.Buffs.AddBuff(buff.BuffId, template);
            template.Hp = template.MaxHp;
            template.Mp = template.MaxMp;

            template.Transform = spawnPos.CloneDetached(template);
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
                    AttachPoint = doodadBinding.AttachPointId,
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
                doodad.Transform = template.Transform.CloneAttached(doodad);
                doodad.Transform.Parent = template.Transform;

                if (_attachPoints.ContainsKey(template.ModelId))
                {
                    if (_attachPoints[template.ModelId].ContainsKey(doodadBinding.AttachPointId))
                    {
                        doodad.Transform = template.Transform.CloneAttached(doodad);
                        doodad.Transform.Parent = template.Transform;
                        doodad.Transform.Local.Translate(_attachPoints[template.ModelId][doodadBinding.AttachPointId]
                            .AsPositionVector());
                        doodad.Transform.Local.SetRotation(
                            _attachPoints[template.ModelId][doodadBinding.AttachPointId].Roll,
                            _attachPoints[template.ModelId][doodadBinding.AttachPointId].Pitch,
                            _attachPoints[template.ModelId][doodadBinding.AttachPointId].Yaw);
                        _log.Debug("Model id: {0} attachment {1} => pos {2} = {3}", template.ModelId,
                            doodadBinding.AttachPointId, _attachPoints[template.ModelId][doodadBinding.AttachPointId],
                            doodad.Transform);
                    }
                    else
                    {
                        _log.Warn("Model id: {0} incomplete attach point information", template.ModelId);
                    }
                }
                else
                {
                    doodad.Transform = new Transform(doodad);
                    _log.Warn("Model id: {0} has no attach point information", template.ModelId);
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
                    ParentObj = template,
                    TemplateId = childSlaveTemplate.Id,
                    Name = childSlaveTemplate.Name,
                    Level = (byte)childSlaveTemplate.Level,
                    ModelId = childSlaveTemplate.ModelId,
                    Template = childSlaveTemplate,
                    Hp = 1,
                    Mp = 1,
                    ModelParams = new UnitCustomModelParams(),
                    Faction = owner.Faction,
                    Id = 11, // TODO
                    Summoner = owner,
                    SpawnTime = DateTime.Now,
                    AttachPointId = (sbyte)slaveBinding.AttachPointId,
                    OwnerObjId = template.ObjId
                };
                childSlave.Hp = childSlave.MaxHp;
                childSlave.Mp = childSlave.MaxMp;
                childSlave.Transform = spawnPos.CloneDetached(childSlave);
                childSlave.Transform.Parent = template.Transform;

                if (_attachPoints.ContainsKey(template.ModelId))
                {
                    if (_attachPoints[template.ModelId].ContainsKey(slaveBinding.AttachPointId))
                    {
                        var attachPoint = _attachPoints[template.ModelId][slaveBinding.AttachPointId];
                        // childSlave.AttachPosition = _attachPoints[template.ModelId][(int) slaveBinding.AttachPointId];
                        childSlave.Transform = template.Transform.CloneAttached(childSlave);
                        childSlave.Transform.Parent = template.Transform;
                        childSlave.Transform.Local.Translate(attachPoint.AsPositionVector());
                        childSlave.Transform.Local.Rotate(attachPoint.Roll, attachPoint.Pitch, attachPoint.Yaw);
                    }
                    else
                    {
                        _log.Warn("Model id: {0} incomplete attach point information");
                    }
                }

                template.AttachedSlaves.Add(childSlave);
                _tlSlaves.Add(childSlave.TlId, childSlave);
                childSlave.Spawn();
            }

            _tlSlaves.Add(template.TlId, template);
            _activeSlaves.Add(owner.ObjId, template);

            if (slaveTemplate.IsABoat())
                BoatPhysicsManager.Instance.AddShip(template);

            owner.SendPacket(new SCMySlavePacket(template.ObjId, template.TlId, template.Name, template.TemplateId,
                template.Hp, template.Mp,
                template.Transform.World.Position.X, template.Transform.World.Position.Y,
                template.Transform.World.Position.Z));
        }

        public void LoadSlaveAttachmentPointLocations()
        {
            _log.Info("Loading Slave Model Attach Points...");

            var filePath = Path.Combine(FileManager.AppPath, "Data", "slave_attach_points.json");
            var contents = FileManager.GetFileContents(filePath);
            if (string.IsNullOrWhiteSpace(contents))
                throw new IOException(
                    $"File {filePath} doesn't exists or is empty.");

            List<SlaveModelAttachPoint> attachPoints;
            if (JsonHelper.TryDeserializeObject(contents, out attachPoints, out _))
                _log.Info("Slave model attach points loaded...");
            else
                _log.Warn("Slave model attach points not loaded...");

            // Convert degrees from json to radian
            foreach (var vehicle in attachPoints)
            {
                foreach (var pos in vehicle.AttachPoints)
                {
                    pos.Value.Roll = pos.Value.Roll.DegToRad();
                    pos.Value.Pitch = pos.Value.Pitch.DegToRad();
                    pos.Value.Yaw = pos.Value.Yaw.DegToRad();
                }
            }

            _attachPoints = new Dictionary<uint, Dictionary<AttachPointKind, WorldSpawnPosition>>();
            foreach (var set in attachPoints)
            {
                _attachPoints[set.ModelId] = set.AttachPoints;
            }
        }

        public void Load()
        {
            _slaveTemplates = new Dictionary<uint, SlaveTemplate>();
            _activeSlaves = new Dictionary<uint, Slave>();
            _tlSlaves = new Dictionary<uint, Slave>();
            _slaveInitialItems = new Dictionary<uint, List<SlaveInitialItems>>();

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
                                Name =
                                    LocalizationManager.Instance.Get("slaves", "name", reader.GetUInt32("id"),
                                        reader.GetString("name")),
                                ModelId = reader.GetUInt32("model_id"),
                                Mountable = reader.GetBoolean("mountable"),
                                SpawnXOffset = reader.GetFloat("spawn_x_offset"),
                                SpawnYOffset = reader.GetFloat("spawn_y_offset"),
                                FactionId = reader.GetUInt32("faction_id", 0),
                                Level = reader.GetUInt32("level"),
                                Cost = reader.GetInt32("cost"),
                                SlaveKind = (SlaveKind)reader.GetUInt32("slave_kind_id"),
                                SpawnValidAreaRance = reader.GetUInt32("spawn_valid_area_range", 0),
                                SlaveInitialItemPackId = reader.GetUInt32("slave_initial_item_pack_id", 0),
                                SlaveCustomizingId = reader.GetUInt32("slave_customizing_id", 0),
                                Customizable = reader.GetBoolean("customizable", false),
                                PortalTime = reader.GetFloat("portal_time")
                            };
                            _slaveTemplates.Add(template.Id, template);
                        }
                    }
                }
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM slave_initial_items";
                    command.Prepare();

                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var ItemPackId = reader.GetUInt32("slave_initial_item_pack_id");
                            var SlotId = reader.GetByte("equip_slot_id");
                            var item = reader.GetUInt32("item_id");
                            
                            if (_slaveInitialItems.TryGetValue(ItemPackId,out var key))
                            {
                                key.Add(new SlaveInitialItems() { slaveInitialItemPackId = ItemPackId, equipSlotId = SlotId, itemId = item});
                            }
                            else
                            {
                                var newPack = new List<SlaveInitialItems>();
                                var newKey = new SlaveInitialItems()
                                {
                                    slaveInitialItemPackId = ItemPackId, equipSlotId = SlotId, itemId = item
                                };
                                newPack.Add(newKey);
                                
                                _slaveInitialItems.Add(ItemPackId, newPack);
                            }
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
                                AttachPointId = (AttachPointKind)reader.GetInt32("attach_point_id"),
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
                                AttachPointId = (AttachPointKind)reader.GetUInt32("attach_point_id"),
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


            LoadSlaveAttachmentPointLocations();
        }

        public void Initialize()
        {
            var sendMySlaveTask = new SendMySlaveTask();
            TaskManager.Instance.Schedule(sendMySlaveTask, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        }

        public void SendMySlavePacketToAllOwners()
        {
            foreach (var (ownerObjId, slave) in _activeSlaves)
            {
                var owner = WorldManager.Instance.GetCharacterByObjId(ownerObjId);
                owner?.SendPacket(new SCMySlavePacket(slave.ObjId, slave.TlId, slave.Name, slave.TemplateId, slave.Hp,
                    slave.Mp,
                    slave.Transform.World.Position.X, slave.Transform.World.Position.Y,
                    slave.Transform.World.Position.Z));
            }
        }

        public Slave GetIsMounted(uint objId, out AttachPointKind attachPoint)
        {
            attachPoint = AttachPointKind.None;
            foreach (var slave in _activeSlaves)
            foreach (var unit in slave.Value.AttachedCharacters)
            {
                if (unit.Value.ObjId == objId)
                {
                    attachPoint = unit.Key;
                    return slave.Value;
                }
            }
            return null;
        }
    }

    public class SlaveModelAttachPoint
    {
        public uint ModelId;
        public Dictionary<AttachPointKind, WorldSpawnPosition> AttachPoints;
    }
}
