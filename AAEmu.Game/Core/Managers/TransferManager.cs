using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.Units;
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
        public Dictionary<uint, Dictionary<int, Point>> _attachPoints;

        public bool Exist(uint templateId)
        {
            return _templates.ContainsKey(templateId);
        }

        //public void SpawnAll()
        //{
        //    foreach (var tr in _activeTransfers.Values)
        //    {
        //        //tr.Spawn();
        //        tr.SendPacket(new SCUnitStatePacket(tr));
        //        tr.SendPacket(new SCUnitPointsPacket(tr.ObjId, tr.Hp, tr.Mp));
        //        if (tr.AttachedDoodads.Count > 0)
        //        {
        //            var doodads = tr.AttachedDoodads.ToArray();
        //            for (var i = 0; i < doodads.Length; i += 30)
        //            {
        //                var count = doodads.Length - i;
        //                var temp = new Doodad[count <= 30 ? count : 30];
        //                Array.Copy(doodads, i, temp, 0, temp.Length);
        //                tr.SendPacket(new SCDoodadsCreatedPacket(temp));
        //            }
        //        }
        //    }
        //}
        public void SpawnAll(Character character)
        {
            foreach (var tr in _activeTransfers.Values)
            {
                //tr.Spawn();
                character.SendPacket(new SCUnitStatePacket(tr));
                character.SendPacket(new SCUnitPointsPacket(tr.ObjId, tr.Hp, tr.Mp));
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

        //public Transfer Create(uint objectId, uint id)
        //{
        //    if (!_templates.ContainsKey(id))
        //        return null;

        //    var template = _templates[id];

        //    var transfer = new Transfer();
        //    transfer.ObjId = objectId > 0 ? objectId : ObjectIdManager.Instance.GetNextId();
        //    transfer.TemplateId = id;
        //    transfer.Template = template;
        //    transfer.ModelId = template.ModelId;
        //    transfer.Faction = FactionManager.Instance.GetFaction(template.FactionId);
        //    transfer.Level = template.Level;
        //    transfer.Patrol = null;

        //    transfer.Hp = transfer.MaxHp;
        //    transfer.Mp = transfer.MaxMp;

        //    _activeTransfers.Add(transfer.ObjId, transfer);

        //    return transfer;
        //}


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

            // create the cab of the carriage.
            var Carriage = GetTransferTemplate(templateId); // 6 - Salislead Peninsula ~ Liriot Hillside Loop Carriage
            var owner = new Transfer();
            owner.Name = "хоупфорд-лес";
            owner.TlId = (ushort)TlIdManager.Instance.GetNextId();
            owner.ObjId = objectId == 0 ? ObjectIdManager.Instance.GetNextId() : objectId;
            owner.OwnerId = 255;
            owner.Spawner = spawner;
            owner.TemplateId = Carriage.Id;   // templateId
            owner.ModelId = Carriage.ModelId; // modelId
            owner.Template = Carriage;
            owner.BondingObjId = 0;    // objId
            owner.AttachPointId = 255; // point
            owner.Level = 1;
            owner.Hp = owner.MaxHp = 19000;
            owner.Mp = owner.MaxMp = 12000;
            owner.ModelParams = new UnitCustomModelParams();
            owner.Position = spawner.Position.Clone();
            owner.Faction = new SystemFaction();
            owner.Patrol = null;
            // create Carriage like a normal object.
            //owner.Spawn(); // in theory already spawned in SpawnManager
            _activeTransfers.Add(owner.ObjId, owner);

            if (Carriage.TransferBindings.Count <= 0) { return owner; }

            var boardingPart = GetTransferTemplate(Carriage.TransferBindings[0].TransferId); // 46 - The wagon boarding part
            var transfer = new Transfer();
            transfer.Name = "дилижанс";
            transfer.TlId = (ushort)TlIdManager.Instance.GetNextId();
            transfer.ObjId = ObjectIdManager.Instance.GetNextId();
            transfer.OwnerId = 255;
            transfer.Spawner = spawner;
            transfer.TemplateId = boardingPart.Id;   // templateId
            transfer.ModelId = boardingPart.ModelId; // modelId
            transfer.Template = boardingPart;
            transfer.Level = 1;
            transfer.BondingObjId = owner.ObjId;
            transfer.AttachPointId = owner.Template.TransferBindings[0].AttachPointId;
            transfer.Hp = transfer.MaxHp = 19000;
            transfer.Mp = transfer.MaxMp = 12000;
            transfer.ModelParams = new UnitCustomModelParams();
            transfer.Position = new Point();
            float newX;
            float newY;
            transfer.Position = spawner.Position.Clone();
            // spawn in front of you
            (newX, newY) = MathUtil.AddDistanceToFront(8.8f, spawner.Position.X, spawner.Position.Y, spawner.Position.RotationZ);
            transfer.Position.Y = newY;
            transfer.Position.X = newX;
            transfer.Position.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(transfer.Position.ZoneId, transfer.Position.X, transfer.Position.Y) : spawner.Position.Z;
            transfer.Position.RotationZ = spawner.Position.RotationZ;
            transfer.Faction = new SystemFaction();
            transfer.Patrol = null;
            // create a boardingPart and indicate that we attach to the Carriage object 
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
                        doodad.Position = new Point(0.00390625f, 5.785156f, 1.367f, 0, 0, 0);
                        break;
                    case 3:
                        doodad.Position = new Point(0.00390625f, 1.634766f, 1.367f, 0, 0, -1);
                        break;
                }
                doodad.Position.WorldId = spawner.Position.WorldId;
                doodad.Position.ZoneId = spawner.Position.ZoneId;

                doodad.OwnerId = 0;
                doodad.PlantTime = DateTime.Now;
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
                            var template = new TransferBindings
                            {
                                Id = reader.GetUInt32("id"),
                                OwnerId = reader.GetUInt32("owner_id"),
                                OwnerType = reader.GetString("owner_type"),
                                AttachPointId = reader.GetByte("attach_point_id"),
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
                                AttachPointId = reader.GetInt32("attach_point_id"),
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
                                _templates[template.OwnerId].TransferPaths.Add(template);
                            }
                        }
                    }
                }
            }
            #endregion
        }
    }
}
