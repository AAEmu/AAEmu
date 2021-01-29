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
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class TransferManager : Singleton<TransferManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, TransferTemplate> _templates;
        private Dictionary<uint, Transfer> _activeTransfers;

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

        public Transfer Create(uint objectId, uint templateId)
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

            // создаем кабину повозки
            var Carriage = GetTransferTemplate(templateId); // 6 - Salislead Peninsula ~ Liriot Hillside Loop Carriage
            var tlId = (ushort)TlIdManager.Instance.GetNextId();
            var objId = objectId == 0 ? ObjectIdManager.Instance.GetNextId() : objectId;
            var owner = new Transfer();
            owner.Name = "хоупфорд-лес";
            owner.TlId = tlId;
            owner.ObjId = objId;
            owner.OwnerId = 0;
            owner.TemplateId = Carriage.Id;   // templateId
            owner.ModelId = Carriage.ModelId; // modelId
            owner.Template = Carriage;
            owner.Name = Carriage.Name;
            //if (owner.Template.TransferBindings != null)
            //{
            //    owner.BondingObjId 
            //}
            owner.BondingObjId = 0;    // objId
            owner.AttachPointId = 255; // point
            owner.Level = 1;
            owner.Hp = 19000;
            owner.Mp = 12000;
            owner.ModelParams = new UnitCustomModelParams();
            owner.Position = new Point();
            owner.Faction = new SystemFaction();
            owner.Patrol = null;
            owner.Faction = FactionManager.Instance.GetFaction(143);

            // create Carriage like a normal object.
            _activeTransfers.Add(owner.ObjId, owner);

            if (Carriage.TransferBindings.Count > 0)
            {
                var boardingPart = GetTransferTemplate(Carriage.TransferBindings[0].TransferId); // 46 - The wagon boarding part
                var tlId2 = (ushort)TlIdManager.Instance.GetNextId();
                var objId2 = ObjectIdManager.Instance.GetNextId();
                var transfer = new Transfer();
                transfer.Name = "дилижанс";
                transfer.TlId = tlId2;
                transfer.ObjId = objId2;
                transfer.OwnerId = 0;
                transfer.TemplateId = boardingPart.Id;   // templateId
                transfer.ModelId = boardingPart.ModelId; // modelId
                transfer.Template = boardingPart;
                transfer.Name = "";
                transfer.Level = 1;
                transfer.BondingObjId = tlId;
                transfer.AttachPointId = owner.Template.TransferBindings[0].AttachPointId;
                transfer.Hp = 19000;
                transfer.Mp = 12000;
                transfer.ModelParams = new UnitCustomModelParams();
                transfer.Position = new Point();
                transfer.Position.WorldId = 1;
                transfer.Position.ZoneId = 179;
                transfer.Position.X = 15565.78f;
                transfer.Position.Y = 14841.25f;
                transfer.Position.Z = 145.2947f;
                transfer.Position.RotationZ = 63;
                transfer.Faction = new SystemFaction();
                transfer.Patrol = null;
                transfer.Faction = FactionManager.Instance.GetFaction(143);

                //TODO  create a boardingPart and indicate that we attach to the Carriage object 
                _activeTransfers.Add(transfer.ObjId, transfer);


                foreach (var doodadBinding in transfer.Template.TransferBindingDoodads)
                {
                    //var doodad = new Doodad();
                    var doodad = DoodadManager.Instance.Create(0, doodadBinding.DoodadId, transfer);
                    doodad.ObjId = ObjectIdManager.Instance.GetNextId();
                    doodad.TemplateId = doodadBinding.DoodadId;
                    doodad.OwnerObjId = objId2;
                    doodad.ParentObjId = 0;
                    doodad.AttachPoint = (byte)doodadBinding.AttachPointId;
                    doodad.Position = new Point(0.00390625f, 5.785156f, 1.367f, 0, 0, 0);
                    //doodad.Position = new Point(0.00390625f, 1.634766f, 1.367f, 0, 0, 0);
                    doodad.OwnerId = 0;
                    doodad.PlantTime = DateTime.Now;
                    doodad.OwnerType = DoodadOwnerType.System;
                    doodad.DbHouseId = 0;
                    doodad.Template = DoodadManager.Instance.GetTemplate(doodadBinding.DoodadId);
                    doodad.Data = (byte)doodadBinding.AttachPointId;
                    doodad.SetScale(1f);
                    doodad.CurrentPhaseId = doodad.GetFuncGroupId();

                    transfer.AttachedDoodads.Add(doodad);

                    owner.SendPacket(new SCDoodadCreatedPacket(doodad));
                }
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
