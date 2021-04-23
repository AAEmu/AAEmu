using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Xml;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class TransferManager : Singleton<TransferManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, TransferTemplate> _templates;
        private Dictionary<uint, Transfer> _activeTransfers;
        private Dictionary<uint, Transfer> _moveTransfers;
        private Dictionary<byte, Dictionary<uint, List<TransferRoads>>> _transferRoads;
        public Thread thread { get; set; }
        private bool ThreadRunning = true;

        public void Initialize()
        {
            thread = new Thread(TransferThread);
            thread.Start();
        }

        private void TransferThread()
        {
            while (Thread.CurrentThread.IsAlive)
            {
                Thread.Sleep(100);
                var activeTransfers = Instance.GetMoveTransfers();
                foreach (var transfer in activeTransfers)
                {
                    //TransfersTick(transfer);
                    transfer.MoveTo();
                }
            }
        }

        private void TransfersTick(Transfer transfer)
        {
            if (transfer.TimeLeft > 0) { return; } // Пауза в начале/конце пути и на остановках

            var moveType = (TransferData)MoveType.GetType(MoveTypeEnum.Transfer);
            moveType.UseTransferBase(transfer);
            var velAccel = 2.0f; //per s
            var maxVelForward = 9.5f; //per s
            var maxVelBackward = -5.0f;

            transfer.Speed += (transfer.Throttle * 0.00787401575f) * (velAccel / 20f);
            transfer.Speed = Math.Min(transfer.Speed, maxVelForward);
            transfer.Speed = Math.Max(transfer.Speed, maxVelBackward);

            transfer.RotSpeed += (transfer.Steering * 0.00787401575f) * (velAccel / 20f);
            transfer.RotSpeed = Math.Min(transfer.RotSpeed, 1);
            transfer.RotSpeed = Math.Max(transfer.RotSpeed, -1);

            if (transfer.Steering == 0)
                transfer.RotSpeed -= (transfer.RotSpeed / 20);
            if (transfer.Throttle == 0) // this needs to be fixed : ships need to apply a static drag, and slowly ship away at the speed instead of doing it like this
                transfer.Speed -= (transfer.Speed / 45);

            //var slaveRotRad = ypr.Item1 + (90 * (Math.PI / 180.0f));
            //var (rotX, rotY, rotZ) = MathUtil.GetSlaveRotationFromDegrees(ypr.Item3, ypr.Item2, ypr.Item1);
            var vPosition = new Vector3();
            var vTarget = new Vector3();
            var Angle = MathUtil.CalculateDirection(vPosition, vTarget);
            var quat = MathUtil.ConvertRadianToDirectionShort(Angle);
            moveType.Rot = new Quaternion(quat.X, quat.Z, quat.Y, quat.W);

            //moveType.RotationX = rotX;
            //moveType.RotationY = rotY;
            //moveType.RotationZ = rotZ;

            transfer.BroadcastPacket(new SCOneUnitMovementPacket(transfer.ObjId, moveType), false);
            // _log.Debug("Island: {0}", slave.RigidBody.CollisionIsland.Bodies.Count);
        }

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
        public Transfer[] GetMoveTransfers()
        {
            return _moveTransfers.Values.ToArray();
        }

        public void AddMoveTransfers(uint ObjId, Transfer transfer)
        {
            _moveTransfers.Add(ObjId, transfer);
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

        public void Spawn(Character character, Transfer tr)
        {
            // спавним кабину
            character.SendPacket(new SCUnitStatePacket(tr));
            character.SendPacket(new SCUnitPointsPacket(tr.ObjId, tr.Hp, tr.Mp));

            // пробуем спавнить прицеп
            if (tr.Bounded != null)
            {
                character.SendPacket(new SCUnitStatePacket(tr.Bounded));
                character.SendPacket(new SCUnitPointsPacket(tr.Bounded.ObjId, tr.Bounded.Hp, tr.Bounded.Mp));

                if (tr.Bounded.AttachedDoodads.Count > 0)
                {

                    var doodads = tr.Bounded.AttachedDoodads.ToArray();
                    for (var i = 0; i < doodads.Length; i += 30)
                    {
                        var count = doodads.Length - i;
                        var temp = new Doodad[count <= 30 ? count : 30];
                        Array.Copy(doodads, i, temp, 0, temp.Length);
                        character.SendPacket(new SCDoodadsCreatedPacket(temp));
                    }
                }
            }

            // если есть Doodad в кабине
            if (tr.AttachedDoodads.Count > 0)
            {
                var doodads = tr.AttachedDoodads.ToArray();
                for (var i = 0; i < doodads.Length; i += 30)
                {
                    var count = doodads.Length - i;
                    var temp = new Doodad[count <= 30 ? count : 30];
                    Array.Copy(doodads, i, temp, 0, temp.Length);
                    character.SendPacket(new SCDoodadsCreatedPacket(temp));
                }
            }
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

            // create the cab of the carriage
            var owner = new Transfer();
            var Carriage = GetTransferTemplate(templateId); // 6 - Salislead Peninsula ~ Liriot Hillside Loop Carriage
            owner.Name = Carriage.Name;
            owner.TlId = (ushort)TlIdManager.Instance.GetNextId();
            owner.ObjId = objectId == 0 ? ObjectIdManager.Instance.GetNextId() : objectId;
            owner.OwnerId = 255;
            owner.Spawner = spawner;
            owner.TemplateId = Carriage.Id;
            owner.ModelId = Carriage.ModelId;
            owner.Template = Carriage;
            owner.BondingObjId = 0;
            owner.AttachPointId = 255;
            owner.Level = 1;
            owner.Hp = owner.MaxHp;
            owner.Mp = owner.MaxMp;
            owner.ModelParams = new UnitCustomModelParams();
            owner.Position = spawner.Position.Clone();
            owner.Position.RotationZ = spawner.Position.RotationZ;
            owner.Rot = new Quaternion(0f, 0f, spawner.RotationZ, 1f);
            owner.Faction = new SystemFaction();
            //owner.Faction = FactionManager.Instance.GetFaction(1);
            owner.Patrol = null;
            // add effect
            var buffId = 545u; //BUFF: Untouchable (Unable to attack this target)
            owner.Buffs.AddBuff(new Buff(owner, owner, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(buffId), null, System.DateTime.Now));

            // create Carriage like a normal object.
            owner.Spawn();
            _activeTransfers.Add(owner.ObjId, owner);

            if (Carriage.TransferBindings.Count <= 0) { return owner; }

            var boardingPart = GetTransferTemplate(Carriage.TransferBindings[0].TransferId); // 46 - The wagon boarding part
            var transfer = new Transfer();
            transfer.Name = boardingPart.Name;
            transfer.TlId = (ushort)TlIdManager.Instance.GetNextId();
            transfer.ObjId = ObjectIdManager.Instance.GetNextId();
            transfer.OwnerId = 255;
            transfer.Spawner = owner.Spawner;
            transfer.TemplateId = boardingPart.Id;
            transfer.ModelId = boardingPart.ModelId;
            transfer.Template = boardingPart;
            transfer.Level = 1;
            transfer.BondingObjId = owner.ObjId;
            transfer.AttachPointId = owner.Template.TransferBindings[0].AttachPointId;
            transfer.Hp = transfer.MaxHp;
            transfer.Mp = transfer.MaxMp;
            transfer.ModelParams = new UnitCustomModelParams();
            transfer.Position = spawner.Position.Clone();
            transfer.Position.RotationZ = spawner.Position.RotationZ;
            transfer.Rot = new Quaternion(0f, 0f, 0, 1f);

            (transfer.Position.X, transfer.Position.Y) = MathUtil.AddDistanceToFront(-9.24417f, transfer.Position.X, transfer.Position.Y, transfer.Position.RotationZ);
            //var tempPoint = new TransfersPathPoint(transfer.Position.WorldId, transfer.Position.ZoneId, -0.33f, -9.01f, 2.44f, 0, 0, 0);
            transfer.Position.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(transfer.Position.ZoneId, transfer.Position.X, transfer.Position.Y) : transfer.Position.Z;

            transfer.Faction = new SystemFaction();
            transfer.Patrol = null;
            // add effect
            buffId = 545u; //BUFF: Untouchable (Unable to attack this target)
            transfer.Buffs.AddBuff(new Buff(transfer, transfer, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(buffId), null, System.DateTime.Now));

            //TODO  create a boardingPart and indicate that we attach to the Carriage object 
            transfer.Spawn();
            _activeTransfers.Add(transfer.ObjId, transfer);


            foreach (var doodadBinding in transfer.Template.TransferBindingDoodads)
            {
                var doodad = DoodadManager.Instance.Create(0, doodadBinding.DoodadId, transfer);
                doodad.ObjId = ObjectIdManager.Instance.GetNextId();
                doodad.TemplateId = doodadBinding.DoodadId;
                doodad.OwnerObjId = 0;
                doodad.ParentObjId = transfer.ObjId;
                doodad.Spawner = new DoodadSpawner();
                doodad.AttachPoint = (byte)doodadBinding.AttachPointId;
                switch (doodadBinding.AttachPointId)
                {
                    case 2:
                        doodad.Position = new Point(owner.Position.WorldId, owner.Position.ZoneId, 0.00537476f, 5.7852f, 1.36648f, 0, 0, 0);
                        break;
                    case 3:
                        doodad.Position = new Point(owner.Position.WorldId, owner.Position.ZoneId, 0.00537476f, 1.63614f, 1.36648f, 0, 0, -1);
                        break;
                }

                doodad.OwnerId = 0;
                doodad.PlantTime = DateTime.UtcNow;
                doodad.OwnerType = DoodadOwnerType.System;
                doodad.DbHouseId = 0;
                doodad.Template = DoodadManager.Instance.GetTemplate(doodadBinding.DoodadId);
                doodad.Data = (byte)doodadBinding.AttachPointId;
                doodad.SetScale(1f);
                doodad.FuncGroupId = doodad.GetFuncGroupId();

                doodad.Spawn();
                transfer.AttachedDoodads.Add(doodad);
            }

            return owner;
        }

        public void Load()
        {
            _templates = new Dictionary<uint, TransferTemplate>();
            _activeTransfers = new Dictionary<uint, Transfer>();
            _moveTransfers = new Dictionary<uint, Transfer>();

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
                            template.Name = LocalizationManager.Instance.Get("transfer", "comment", reader.GetUInt32("id"));
                            template.ModelId = reader.GetUInt32("model_id");
                            template.WaitTime = reader.GetFloat("wait_time");
                            template.Cyclic = reader.GetBoolean("cyclic");
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
                            var template = new TransferBindings();
                            //template.Id = reader.GetUInt32("id");
                            template.OwnerId = reader.GetUInt32("owner_id");
                            template.OwnerType = reader.GetString("owner_type");
                            template.AttachPointId = reader.GetByte("attach_point_id");
                            template.TransferId = reader.GetUInt32("transfer_id");
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
                            var template = new TransferBindingDoodads();
                            //template.Id = reader.GetUInt32("id");
                            template.OwnerId = reader.GetUInt32("owner_id");
                            template.OwnerType = reader.GetString("owner_type");
                            template.AttachPointId = reader.GetInt32("attach_point_id");
                            template.DoodadId = reader.GetUInt32("doodad_id");
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
                        //var step = 0u;
                        while (reader.Read())
                        {
                            var template = new TransferPaths
                            {
                                //Id = step++,
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
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");

            //                              worldId           key   transfer_path
            _transferRoads = new Dictionary<byte, Dictionary<uint, List<TransferRoads>>>();
            foreach (var world in worlds)
            {
                var transferPaths = new Dictionary<uint, List<TransferRoads>>();
                for (uint zoneId = 129; zoneId < 346; zoneId++)
                {
                    var contents = FileManager.GetFileContents($"{FileManager.AppPath}Data/Worlds/{world.Name}/level_design/zone/{zoneId}/client/transfer_path.xml");
                    if (string.IsNullOrWhiteSpace(contents))
                    {
                        //_log.Warn($"{FileManager.AppPath}Data/Worlds/{world.Name}/level_design/zone/{zoneId}/client/transfer_path.xml doesn't exists or is empty.");
                        continue;
                    }

                    var transferPath = new List<TransferRoads>();
                    var xDoc = new XmlDocument();
                    xDoc.Load($"{FileManager.AppPath}Data/Worlds/{world.Name}/level_design/zone/{zoneId}/client/transfer_path.xml");
                    var xRoot = xDoc.DocumentElement;
                    foreach (XmlElement xnode in xRoot)
                    {
                        var transferRoad = new TransferRoads();
                        if (xnode.Attributes.Count > 0)
                        {
                            transferRoad.Name = xnode.Attributes.GetNamedItem("Name").Value;
                            transferRoad.ZoneId = zoneId;
                            transferRoad.Type = int.Parse(xnode.Attributes.GetNamedItem("Type").Value);
                            transferRoad.CellX = int.Parse(xnode.Attributes.GetNamedItem("cellX").Value);
                            transferRoad.CellY = int.Parse(xnode.Attributes.GetNamedItem("cellY").Value);
                        }
                        foreach (XmlNode childnode in xnode.ChildNodes)
                        {
                            foreach (XmlNode node in childnode.ChildNodes)
                            {
                                if (node.Attributes.Count > 0)
                                {
                                    var attributeValue = node.Attributes.GetNamedItem("Pos").Value;
                                    var splitVals = attributeValue.Split(',');
                                    if (splitVals.Length == 3)
                                    {
                                        var x = float.Parse(splitVals[0]);
                                        var y = float.Parse(splitVals[1]);
                                        var z = float.Parse(splitVals[2]);
                                        // конвертируем координаты из локальных в мировые, сразу при считывании из файла пути
                                        var xyz = new Vector3(x, y, z);
                                        var (xx, yy, zz) = ZoneManager.Instance.ConvertToWorldCoordinates(zoneId, xyz);
                                        var pos = new Point(xx, yy, zz)
                                        {
                                            WorldId = world.Id,
                                            ZoneId = zoneId
                                        };
                                        transferRoad.Pos.Add(pos);
                                    }
                                }
                            }
                        }
                        transferPath.Add(transferRoad);
                    }
                    transferPaths.Add(zoneId, transferPath);
                }
                _transferRoads.Add((byte)world.Id, transferPaths);
            }
            #endregion

            GetOwnerPaths();
        }
        /// <summary>
        /// Получить список всех частей своего пути для транспорта
        /// </summary>
        /// <param name="worldId"></param>
        /// <returns></returns>
        private void GetOwnerPaths(byte worldId = 1)
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
        internal void Stop()
        {
            ThreadRunning = false;
        }
    }
}
