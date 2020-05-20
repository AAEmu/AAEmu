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
using AAEmu.Game.Models.Game.Skills;
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

        public void UnbindTransfer(GameConnection connection, uint tlId)
        {
            var unit = connection.ActiveChar;
            var activeTransferInfo = GetActiveTransferByTlId(tlId);
            if (activeTransferInfo == null) { return; }
            unit.SendPacket(new SCUnitDetachedPacket(unit.ObjId, 5));
        }

        public void BindTransfer(GameConnection connection, uint tlId)
        {
            var unit = connection.ActiveChar;
            var activeTransferInfo = GetActiveTransferByTlId(tlId);
            if (activeTransferInfo == null)
            {
                return;
            }
            unit.SendPacket(new SCUnitAttachedPacket(unit.ObjId, 1, 6, activeTransferInfo.ObjId));
            unit.BroadcastPacket(new SCTargetChangedPacket(unit.ObjId, activeTransferInfo.ObjId), true);
            unit.CurrentTarget = activeTransferInfo;
            unit.SendPacket(new SCSlaveBoundPacket(unit.Id, activeTransferInfo.ObjId));
        }

        public void Delete(Character owner, uint objId)
        {
            var activeTransferInfo = GetActiveTransferByObjId(objId);
            if (activeTransferInfo == null)
            {
                return;
            }
            owner.SendPacket(new SCSlaveDespawnPacket(objId));
            owner.SendPacket(new SCSlaveRemovedPacket(owner.ObjId, activeTransferInfo.TlId));
            _activeTransfers.Remove(owner.ObjId);
            activeTransferInfo.Delete();
        }

        //public Transfer Create(uint objectId, uint id)
        //{
        //    if (!_templates.ContainsKey(id)) { return null; }

        //    var template = _templates[id];
        //    var transfer = new Transfer
        //    {
        //        ObjId = objectId > 0 ? objectId : ObjectIdManager.Instance.GetNextId(),
        //        TemplateId = id,
        //        Template = template,
        //        ModelId = template.ModelId,
        //        Patrol = null
        //    };
        //    //transfer.TemplateId = 83; // Long sandbar hunting airship
        //    //transfer.ModelId = 657;
        //    transfer.ObjId = ObjectIdManager.Instance.GetNextId();
        //    transfer.TlId = (ushort)TlIdManager.Instance.GetNextId();
        //    transfer.Level = 50;
        //    transfer.Position = new Point();
        //    transfer.Hp = transfer.MaxHp = 19000;
        //    transfer.Mp = transfer.MaxMp = 12000;
        //    transfer.ModelParams = new UnitCustomModelParams();

        //    return transfer;
        //}

        public Transfer Create(uint objectId, uint templateId)
        {
            /*
            * Последовательность пакетов при появлении повозки:
            * (сама повозка состоит из двух частей и двух скамеек для сидения персонажей)
            * SCUnitStatePacket(tlId0=GetNextId(), objId0=GetNextId(), templateId = 6, modelId = 654, attachPoint=255)
            * SCUnitStatePacket(tlId2= tlId0, objId2=GetNextId(), templateId = 46, modelId = 653, attachPoint=30, objId=objId0)
            * SCDoodadCreatedPacket(templateId = 5890, attachPoint=2, objId=objId2, x1y1z1)
            * SCDoodadCreatedPacket(templateId = 5890, attachPoint=3, objId=objId2, x2y2z2)
            */

            if (!Exist(templateId)) { return null; }
            var Carriage = GetTransferTemplate(templateId); // 6 - Salislead Peninsula ~ Liriot Hillside Loop Carriage

            var tlId = (ushort)TlIdManager.Instance.GetNextId();
            var objId = objectId == 0 ? ObjectIdManager.Instance.GetNextId() : objectId;
            var owner = new Transfer()
            {
                TlId = tlId,
                ObjId = objId,
                TemplateId = Carriage.Id,   // templateId
                ModelId = Carriage.ModelId, // modelId
                Template = Carriage,
                Name = "",
                Level = 50,
                Hp = 19000,
                Mp = 12000,
                ModelParams = new UnitCustomModelParams(),
                Position = new Point(),
                Faction = new SystemFaction(),
                Patrol = null
            };
            // создаем Carriage, как обычный объект
            //owner.SendPacket(new SCUnitStatePacket(owner));
            //owner.Spawn();

            //_activeTransfers.Add(objId, owner);

            var boardingPart = GetTransferTemplate(templateId); // 46 - The wagon boarding part
            var tlId2 = (ushort)TlIdManager.Instance.GetNextId();
            var objId2 = objectId == 0 ? ObjectIdManager.Instance.GetNextId() : objectId;
            var transfer = new Transfer()
            {
                TlId = tlId2,
                ObjId = objId2,
                TemplateId = boardingPart.Id,   // templateId
                ModelId = boardingPart.ModelId, // modelId
                Template = boardingPart,
                Name = "",
                Level = 50,
                Hp = 19000,
                Mp = 12000,
                ModelParams = new UnitCustomModelParams(),
                Patrol = null
            };
            //transfer.Spawn();

            //_activeTransfers.Add(objId, transfer);
            //TODO  создаем boardingPart и указываем, что прикрепляем к Carriage объекту 
            //owner.SendPacket(new SCUnitStatePacket(tlId, objId, transfer, (byte)transfer.Template.TransferBindings[0].AttachPointId));

            foreach (var doodadBinding in owner.Template.TransferBindingDoodads)
            {
                var doodad = new Doodad
                {
                    ObjId = ObjectIdManager.Instance.GetNextId(),
                    TemplateId = doodadBinding.DoodadId,
                    OwnerObjId = owner.ObjId,
                    ParentObjId = 0,
                    AttachPoint = (byte)doodadBinding.AttachPointId,
                    Position = new Point(0f, 3.204f, 12588.96f, 0, 0, 0),
                    OwnerId = doodadBinding.OwnerId,
                    PlantTime = DateTime.Now,
                    OwnerType = DoodadOwnerType.System,
                    DbHouseId = 0,
                    Template = DoodadManager.Instance.GetTemplate(doodadBinding.DoodadId),
                    Data = (byte)doodadBinding.AttachPointId
                };
                doodad.SetScale(1f);
                doodad.FuncGroupId = doodad.GetGroupId();
                owner.SendPacket(new SCDoodadCreatedPacket(doodad));
            }

            _activeTransfers.Add(objId, owner);

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
                            var template = new TransferTemplate
                            {
                                Id = reader.GetUInt32("id"),
                                Name = LocalizationManager.Instance.Get("transfer", "comment", reader.GetUInt32("id"), ItemManager.Instance.GetTemplate(reader.GetUInt32("id")).Name ?? ""),
                                ModelId = reader.GetUInt32("model_id"),
                                WaitTime = reader.GetFloat("wait_time"),
                                Cyclic = reader.GetBoolean("cyclic"),
                                PathSmoothing = reader.GetFloat("path_smoothing"),
                            };
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
                                AttachPointId = reader.GetUInt32("attach_point_id"),
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

                            foreach (var tmp in _templates)
                            {
                                foreach (var tmp2 in tmp.Value.TransferBindings)
                                {
                                    if (_templates.ContainsKey(tmp2.TransferId))
                                    {
                                        _templates[tmp2.TransferId].TransferBindingDoodads.Add(template);
                                    }
                                }
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
