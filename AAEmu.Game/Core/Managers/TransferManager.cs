using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Xml;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Models.Tasks.Transfers;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;
using NLog;
using XmlHelper = AAEmu.Commons.Utils.XML.XmlHelper;

namespace AAEmu.Game.Core.Managers
{
    public class TransferManager : Singleton<TransferManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private bool _initialized = false;

        private Dictionary<uint, TransferTemplate> _templates;
        private Dictionary<uint, Transfer> _activeTransfers;
        private Dictionary<byte, Dictionary<uint, List<TransferRoads>>> _transferRoads;
        private const double Delay = 100;
        private const double DelayInit = 1;
        private Task TransferTickTask { get; set; }

        public void Initialize()
        {
            if (_initialized)
                return;
            
            _log.Warn("TransferTickTask: Started");

            //TransferTickTask = new TransferTickStartTask();
            //TaskManager.Instance.Schedule(TransferTickTask, TimeSpan.FromMinutes(DelayInit), TimeSpan.FromMilliseconds(Delay));
            
            TickManager.Instance.OnTick.Subscribe(TransferTick, TimeSpan.FromMilliseconds(Delay), true);

            _initialized = true;
        }

        internal void TransferTick(TimeSpan delta)
        {
            var activeTransfers = GetTransfers();
            foreach (var transfer in activeTransfers)
            {
                transfer.MoveTo(transfer);
            }

            //TaskManager.Instance.Schedule(TransferTickTask, TimeSpan.FromMilliseconds(Delay));
        }
        internal void TransferTick()
        {
            var activeTransfers = GetTransfers();
            foreach (var transfer in activeTransfers)
            {
                transfer.MoveTo(transfer);
            }

            //TaskManager.Instance.Schedule(TransferTickTask, TimeSpan.FromMilliseconds(Delay));
        }

        //public void AddMoveTransfers(uint ObjId, Transfer transfer)
        //{
        //    _moveTransfers.Add(ObjId, transfer);
        //}

        public bool Exist(uint templateId)
        {
            return _templates.ContainsKey(templateId);
        }

        public void SpawnAll()
        {
            foreach (var tr in _activeTransfers.Values)
            {
                tr.Spawn();
            }
        }

        public Transfer[] GetTransfers()
        {
            return _activeTransfers.Values.ToArray();
        }

        public TransferTemplate GetTemplate(uint templateId)
        {
            return _templates.ContainsKey(templateId) ? _templates[templateId] : null;
        }

        private TransferTemplate GetTransferTemplate(uint id)
        {
            return _templates.ContainsKey(id) ? _templates[id] : null;
        }

        private Transfer GetActiveTransferBiTemplateId(uint id)
        {
            return _activeTransfers.ContainsKey(id) ? _activeTransfers[id] : null;
        }

        private Transfer GetActiveTransferByOwnerObjId(uint objId)
        {
            return _activeTransfers.ContainsKey(objId) ? _activeTransfers[objId] : null;
        }

        private Transfer GetActiveTransferByObjId(uint objId)
        {
            foreach (var tr in _activeTransfers.Values)
            {
                if (tr.ObjId == objId)
                {
                    return tr;
                }
            }

            return null;
        }

        private Transfer GetActiveTransferByTlId(uint tlId)
        {
            foreach (var transfer in _activeTransfers.Values)
            {
                if (transfer.TlId == tlId)
                {
                    return transfer;
                }
            }

            return null;
        }

        public Transfer Create(uint objectId, uint templateId, TransferSpawner spawner)
        {
            /*
            * A sequence of packets when a cart appears:
            * (the wagon itself consists of two parts and two benches for the characters)
            * "Salislead Peninsula ~ Liriot Hillside Loop Carriage"
            * SCUnitStatePacket(tlId0=GetNextId(), objId0=GetNextId(), templateId = 6, modelId = 654, attachPoint=255)
            * "The wagon boarding part"
            * SCUnitStatePacket(tlId2= tlId0, objId2=GetNextId(), templateId = 46, modelId = 653, attachPoint=30, objId=objId0)
            * SCDoodadCreatedPacket(templateId = 5890, attachPoint=2, objId=objId2, x1y1z1)
            * SCDoodadCreatedPacket(templateId = 5890, attachPoint=3, objId=objId2, x2y2z2)
            */

            if (!Exist(templateId)) { return null; }

            // create a wagon cabin
            var owner = new Transfer();
            var Carriage = GetTransferTemplate(templateId); // 6 - Salislead Peninsula ~ Liriot Hillside Loop Carriage
            owner.Name = Carriage.Name;
            owner.TlId = (ushort)TlIdManager.Instance.GetNextId();
            owner.ObjId = objectId == 0 ? ObjectIdManager.Instance.GetNextId() : objectId;
            owner.OwnerId = 255;
            owner.Spawner = spawner;
            owner.TemplateId = Carriage.Id;
            owner.Id = Carriage.Id;
            owner.ModelId = Carriage.ModelId;
            owner.Template = Carriage;
            owner.AttachPointId = AttachPointKind.System ;
            owner.BondingObjId = 0;
            owner.Level = 1;
            owner.Hp = owner.MaxHp;
            owner.Mp = owner.MaxMp;
            owner.ModelParams = new UnitCustomModelParams();
            owner.Bounded = null;
            owner.Transform.ApplyWorldSpawnPosition(spawner.Position);
            owner.Transform.ResetFinalizeTransform();
            //owner.Position.RotationZ = MathUtil.ConvertDegreeToDirection(MathUtil.RadianToDegree(spawner.RotationZ));
            //var quat = Quaternion.CreateFromYawPitchRoll(spawner.RotationZ, 0.0f, 0.0f);

            owner.Faction = FactionManager.Instance.GetFaction(FactionsEnum.PcFriendly); // formerly set to 164
            
            owner.Patrol = null;
            // BUFF: Untouchable (Unable to attack this target)
            var buffId = (uint)BuffConstants.Untouchable; 
            owner.Buffs.AddBuff(new Buff(owner, owner, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow));
            owner.Spawn();
            _activeTransfers.Add(owner.ObjId, owner);

            // Add additional transfer units if defined (like a Carriage/Boarding Part for example)
            if (Carriage.TransferBindings.Count <= 0) { return owner; }

            var boardingPart = GetTransferTemplate(Carriage.TransferBindings[0].TransferId); // 46 - The wagon boarding part
            var transfer = new Transfer();
            transfer.Name = boardingPart.Name;
            transfer.TlId = owner.TlId; // (ushort)TlIdManager.Instance.GetNextId();
            transfer.ObjId = ObjectIdManager.Instance.GetNextId();
            transfer.OwnerId = owner.ObjId;
            transfer.Spawner = owner.Spawner;
            transfer.TemplateId = boardingPart.Id;
            transfer.Id = boardingPart.Id;
            transfer.ModelId = boardingPart.ModelId;
            transfer.Template = boardingPart;
            transfer.Level = 1;
            // Attach it to master
            transfer.AttachPointId = owner.Template.TransferBindings[0].AttachPointId;
            transfer.BondingObjId = owner.ObjId;
            transfer.Hp = transfer.MaxHp;
            transfer.Mp = transfer.MaxMp;
            transfer.ModelParams = new UnitCustomModelParams();
            transfer.Transform.ApplyWorldSpawnPosition(spawner.Position);
            //transfer.Position.RotationZ = MathUtil.ConvertDegreeToDirection(MathUtil.RadianToDegree(spawner.RotationZ));
            //var quat2 = Quaternion.CreateFromYawPitchRoll(spawner.RotationZ, 0.0f, 0.0f);
            transfer.Transform.Local.AddDistanceToFront(-9.24417f);
            transfer.Transform.Local.SetHeight(WorldManager.Instance.GetHeight(transfer.Transform));
            transfer.Transform.StickyParent = owner.Transform; // stick it to the driver/motor
            transfer.Transform.ResetFinalizeTransform();

            transfer.Faction = FactionManager.Instance.GetFaction(FactionsEnum.PcFriendly); // used to be 164
            
            transfer.Patrol = null;
            // add effect
            buffId = (uint)BuffConstants.Untouchable; // Buff: Unable to attack this target
            transfer.Buffs.AddBuff(new Buff(transfer, transfer, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow));

            owner.Bounded = transfer; // запомним параметры связанной части в родителе

            transfer.Spawn();
            _activeTransfers.Add(transfer.ObjId, transfer);
            
            foreach (var doodadBinding in transfer.Template.TransferBindingDoodads)
            {
                var doodad = DoodadManager.Instance.Create(0, doodadBinding.DoodadId, transfer);
                doodad.Transform.StickyParent = null;
                doodad.Transform.Parent = transfer.Transform;
                doodad.ParentObjId = transfer.ObjId;
                //doodad.Spawner = new DoodadSpawner();
                doodad.AttachPoint = doodadBinding.AttachPointId;
                switch (doodadBinding.AttachPointId)
                {
                    case AttachPointKind.Passenger0:
                        doodad.Transform.Local.SetPosition(0.00537476f, 5.7852f, 1.36648f, 0, 0, MathF.PI * 2f);
                        break;
                    case AttachPointKind.Passenger1:
                        doodad.Transform.Local.SetPosition(0.00537476f, 1.63614f, 1.36648f, 0, 0, 0);
                        break;
                }
                doodad.Transform.ResetFinalizeTransform();
                doodad.PlantTime = DateTime.UtcNow;
                doodad.Data = (byte)doodadBinding.AttachPointId;
                doodad.SetScale(1f);
                doodad.FuncGroupId = doodad.GetFuncGroupId();
                doodad.OwnerType = DoodadOwnerType.System;
                doodad.Spawn();
                transfer.AttachedDoodads.Add(doodad);
            }

            return owner;
        }

        public void Load()
        {
            _templates = new Dictionary<uint, TransferTemplate>();
            _activeTransfers = new Dictionary<uint, Transfer>();
            
            #region SQLLite

            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM transfers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new TransferTemplate();

                            template.Id = reader.GetUInt32("id"); // OwnerId
                            template.Name = LocalizationManager.Instance.Get("transfers", "comment", reader.GetUInt32("id"), reader.GetString("comment"));
                            template.ModelId = reader.GetUInt32("model_id");
                            template.WaitTime = reader.GetFloat("wait_time");
                            template.Cyclic = reader.GetBoolean("cyclic", true);
                            template.PathSmoothing = reader.GetFloat("path_smoothing");

                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM transfer_bindings";
                    command.Prepare();

                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new TransferBindings
                            {
                                Id = reader.GetUInt32("id"),
                                OwnerId = reader.GetUInt32("owner_id"),
                                OwnerType = reader.GetString("owner_type"),
                                AttachPointId = (AttachPointKind)reader.GetInt16("attach_point_id"),
                                TransferId = reader.GetUInt32("transfer_id")
                            };
                            if (_templates.ContainsKey(template.OwnerId))
                            {
                                _templates[template.OwnerId].TransferBindings.Add(template);
                            }
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM transfer_binding_doodads";
                    command.Prepare();

                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new TransferBindingDoodads
                            {
                                Id = reader.GetUInt32("id"),
                                OwnerId = reader.GetUInt32("owner_id"),
                                OwnerType = reader.GetString("owner_type"),
                                AttachPointId = (AttachPointKind)reader.GetInt32("attach_point_id"),
                                DoodadId = reader.GetUInt32("doodad_id"),
                            };
                            if (_templates.ContainsKey(template.OwnerId))
                            {
                                _templates[template.OwnerId].TransferBindingDoodads.Add(template);
                            }
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM transfer_paths";
                    command.Prepare();

                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new TransferPaths
                            {
                                Id = reader.GetUInt32("id"),
                                OwnerId = reader.GetUInt32("owner_id"),
                                OwnerType = reader.GetString("owner_type"),
                                PathName = reader.GetString("path_name"),
                                WaitTimeStart = reader.GetDouble("wait_time_start"),
                                WaitTimeEnd = reader.GetDouble("wait_time_end")
                            };
                            if (_templates.ContainsKey(template.OwnerId))
                            {
                                _templates[template.OwnerId].TransferAllPaths.Add(template);
                            }
                        }
                    }
                }
            }
            #endregion
            
            #region TransferPath
            _log.Info("Loading transfer_path...");

            var worlds = WorldManager.Instance.GetWorlds();
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

            //                              worldId           key  transfer_path
            _transferRoads = new Dictionary<byte, Dictionary<uint, List<TransferRoads>>>();
            foreach (var world in worlds)
            {
                var transferPaths = new Dictionary<uint, List<TransferRoads>>();
                
                var worldLevelDesignDir = Path.Combine("game", "worlds", world.Name, "level_design", "zone");
                var pathFiles = ClientFileManager.GetFilesInDirectory(worldLevelDesignDir, "transfer_path.xml", true);
                
                foreach(var pathFileName in pathFiles)
                {
                    if (!uint.TryParse(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(pathFileName))),
                        out var zoneId))
                    {
                        _log.Warn("Unable to parse zoneId from {0}", pathFileName);
                        continue;
                    }

                    var contents = ClientFileManager.GetFileAsString(pathFileName);
                    
                    if (string.IsNullOrWhiteSpace(contents))
                    {
                        _log.Warn($"{pathFileName} doesn't exists or is empty.");
                        continue;
                    }

                    _log.Debug($"Loading {pathFileName}");
                    
                    var transferPath = new List<TransferRoads>();
                    var xDoc = new XmlDocument();
                    xDoc.LoadXml(contents);
                    var xRoot = xDoc.DocumentElement;
                    if (xRoot != null)
                    {
                        foreach (XmlElement xNode in xRoot)
                        {
                            var transferRoad = new TransferRoads();
                            var transferAttribs = XmlHelper.ReadNodeAttributes(xNode);

                            transferRoad.ZoneId = zoneId;
                            transferRoad.Name = XmlHelper.ReadAttribute<string>(transferAttribs, "Name","");
                            transferRoad.Type = XmlHelper.ReadAttribute<int>(transferAttribs, "Type",0);
                            transferRoad.CellX = XmlHelper.ReadAttribute<int>(transferAttribs, "cellX",0);
                            transferRoad.CellY = XmlHelper.ReadAttribute<int>(transferAttribs, "cellY",0);

                            foreach (XmlNode childNode in xNode.ChildNodes)
                            {
                                foreach (XmlNode node in childNode.ChildNodes)
                                {
                                    var posNodeAttribs = XmlHelper.ReadNodeAttributes(node);
                                    if (posNodeAttribs.TryGetValue("Pos", out var attributeValue))
                                    {
                                        var xyz = XmlHelper.StringToVector3(attributeValue);

                                        // конвертируем координаты из локальных в мировые, сразу при считывании из файла пути
                                        // convert coordinates from local to world, immediately when reading the path from the file
                                        var vec = ZoneManager.Instance.ConvertToWorldCoordinates(zoneId, xyz);
                                        var pos = new WorldSpawnPosition()
                                        {
                                            X = vec.X,
                                            Y = vec.Y,
                                            Z = vec.Z,
                                            WorldId = world.Id,
                                            ZoneId = zoneId
                                        };
                                        transferRoad.Pos.Add(pos);
                                    }
                                }
                            }

                            transferPath.Add(transferRoad);
                        }
                    }

                    transferPaths.Add(zoneId, transferPath);
                }
                _transferRoads.Add((byte)world.Id, transferPaths);
                GetOwnerPaths(world.Id);
            }
            #endregion
            //GetOwnerPaths();
        }

        /// <summary>
        /// Получить список всех частей своего пути для транспорта
        /// </summary>
        /// <param name="worldId"></param>
        /// <returns></returns>
        private void GetOwnerPaths(uint worldId = 0)
        {
            foreach (var (id, transferTemplate) in _templates)
            {
                foreach (var transferPaths in transferTemplate.TransferAllPaths)
                {
                    foreach (var (wid, transfers) in _transferRoads)
                    {
                        if (wid != worldId) { continue; }
                        foreach (var (zid, transfer) in transfers)
                        {
                            foreach (var path in transfer.Where(path => path.Name == transferPaths.PathName))
                            {
                                var exist = false;
                                foreach (var tr in transferTemplate.TransferRoads.Where(tr => tr.Name == transferPaths.PathName))
                                {
                                    exist = true;
                                }

                                if (exist) { continue; }

                                var tmp = new TransferRoads()
                                {
                                    Name = path.Name,
                                    Type = path.Type,
                                    CellX = path.CellX,
                                    CellY = path.CellY,
                                    Pos = path.Pos
                                };
                                transferTemplate.TransferRoads.Add(tmp);
                            }
                        }
                    }
                }
            }
        }
    }
}
