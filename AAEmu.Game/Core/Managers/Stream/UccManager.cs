using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.S2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Stream;
using AAEmu.Game.Utils.DB;
using NLog;
using NLog.Fluent;

namespace AAEmu.Game.Core.Managers.Stream
{
    public class UccManager : Singleton<UccManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, Ucc> _uploadQueue;
        private Dictionary<uint, SortedList<uint, UccPart>> _complexUploadParts;
        private Dictionary<ulong, Ucc> _uccs;
        private Dictionary<uint, ulong> _downloadQueue; // connection, UCCId
        private static Object lockObject = new object();

        public void Load()
        {
            _uploadQueue = new Dictionary<uint, Ucc>();
            _complexUploadParts = new Dictionary<uint, SortedList<uint, UccPart>>();
            _uccs = new Dictionary<ulong, Ucc>();
            _downloadQueue = new Dictionary<uint, ulong>();

            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM uccs";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var id = reader.GetUInt32("id");
                            var type = (UccType)reader.GetByte("type");
                            if (type == UccType.Simple)
                            {
                                var ucc = new DefaultUcc()
                                {
                                    Id = id,
                                    UploaderId = reader.GetUInt32("uploader_id"),
                                    Pattern1 = reader.GetUInt32("pattern1"),
                                    Pattern2 = reader.GetUInt32("pattern2"),
                                    Color1R = reader.GetUInt32("color1R"),
                                    Color1G = reader.GetUInt32("color1G"),
                                    Color1B = reader.GetUInt32("color1B"),
                                    Color2R = reader.GetUInt32("color2R"),
                                    Color2G = reader.GetUInt32("color2G"),
                                    Color2B = reader.GetUInt32("color2B"),
                                    Color3R = reader.GetUInt32("color3R"),
                                    Color3G = reader.GetUInt32("color3G"),
                                    Color3B = reader.GetUInt32("color3B"),
                                    Modified = reader.GetDateTime("modified")
                                };
                                
                                _uccs.Add(id, ucc);
                            } else if (type == UccType.Complex)
                            {
                                var ucc = new CustomUcc()
                                {
                                    Id = id,
                                    UploaderId = reader.GetUInt32("uploader_id"),
                                    Pattern1 = reader.GetUInt32("pattern1"),
                                    Pattern2 = reader.GetUInt32("pattern2"),
                                    Color1R = reader.GetUInt32("color1R"),
                                    Color1G = reader.GetUInt32("color1G"),
                                    Color1B = reader.GetUInt32("color1B"),
                                    Color2R = reader.GetUInt32("color2R"),
                                    Color2G = reader.GetUInt32("color2G"),
                                    Color2B = reader.GetUInt32("color2B"),
                                    Color3R = reader.GetUInt32("color3R"),
                                    Color3G = reader.GetUInt32("color3G"),
                                    Color3B = reader.GetUInt32("color3B"),
                                    Modified = reader.GetDateTime("modified"),
                                };
                                var uccFileName = Path.Combine(FileManager.AppPath, "UserData", "UCC", id.ToString("000000") + ".dds");
                                if (File.Exists(uccFileName))
                                    ucc.Data.AddRange(File.ReadAllBytes(uccFileName));
                                else
                                    _log.Error("Missing UCC file: {0}", uccFileName);
                                
                                _uccs.Add(id, ucc);
                            }
                        }
                    }
                }
            }
        }

        public void StartUpload(StreamConnection connection)
        {
            _complexUploadParts.Add(connection.Id, new SortedList<uint, UccPart>());
            connection.SendPacket(new TCEmblemStreamRecvStatusPacket(EmblemStreamStatus.Continue));
        }

        public void UploadPart(StreamConnection connection, UccPart part)
        {
            if (!_complexUploadParts.ContainsKey(connection.Id))
                return;
            
            _complexUploadParts[connection.Id].Add(part.Index, part);
        }

        public void AddDefaultUcc(DefaultUcc defaultUcc, StreamConnection connection)
        {
            _uploadQueue.Add(connection.Id, defaultUcc);
            connection.SendPacket(new TCEmblemStreamRecvStatusPacket(EmblemStreamStatus.Start));
        }

        public void CheckUccIsValid(StreamConnection connection, ulong id)
        {
            if (_uccs.ContainsKey(id))
                connection.SendPacket(new TCUccComplexCheckValidPacket(id, false));
        }

        public void UccComplex(StreamConnection connection, ulong id)
        {
            if (_uccs.ContainsKey(id))
                connection.SendPacket(new TCUccComplexPacket(_uccs[id]));
        }

        public void RequestUcc(StreamConnection connection, ulong id)
        {
            _log.Warn("User {0} requesting UCC {1}", connection.GameConnection.ActiveChar.Name, id);
            if (!_uccs.TryGetValue(id, out var ucc))
                return;

            lock (lockObject)
            {
                
                if (_downloadQueue.ContainsKey(connection.Id))
                    if (_downloadQueue[connection.Id] == id)
                    {
                        //_log.Warn("User {0} is already requesting UCC {1}, skipping request !", connection.GameConnection.ActiveChar.Name, id);
                        _log.Warn("User {0} is already requesting UCC {1}, sending first block !", connection.GameConnection.ActiveChar.Name, id);
                        connection.SendPacket(new TCEmblemStreamDownloadPacket(ucc, 0));
                        return;
                    }

                // Update currently downloading UCC Id
                _downloadQueue.Remove(connection.Id);
                _downloadQueue.Add(connection.Id, id);
            }

            if ((ucc is CustomUcc customUcc) && (customUcc.Data.Count > 0))
            {
                connection.SendPacket(new TCEmblemStreamSendStatusPacket(ucc, EmblemStreamStatus.Start));
                //connection.SendPacket(new TCEmblemStreamDownloadPacket(ucc, 0));
            }
            else
            {
                connection.SendPacket(new TCEmblemStreamSendStatusPacket(ucc, EmblemStreamStatus.Continue));
            }

        }
        
        public void RequestUccPart(StreamConnection connection, int previousIndex, int previousSize)
        {
            _log.Warn("User {0} validated UCC part {1} ({2} bytes), sending next part", connection.GameConnection.ActiveChar.Name, previousIndex, previousSize);
            if (!_downloadQueue.TryGetValue(connection.Id, out var uccId))
            {
                _log.Warn("User {0} UCC {1} was not in the request queue", connection.GameConnection.ActiveChar.Name, previousIndex);
                return;
            }

            if (!_uccs.ContainsKey(uccId))
                return;
            var ucc = _uccs[uccId];
            if (!(ucc is CustomUcc customUcc))
                return;

            var index = previousIndex ;
            index++;
            var startPos = index * TCEmblemStreamDownloadPacket.BufferSize;
            var endPos = startPos + TCEmblemStreamDownloadPacket.BufferSize;

            if (startPos >= customUcc.Data.Count)
            {
                lock (lockObject)
                {
                    _downloadQueue.Remove(connection.Id);
                }
                _log.Warn("User {0} UCC {1}, sending end part TCEmblemStreamSendStatusPacket", connection.GameConnection.ActiveChar.Name, customUcc.Id);
                connection.SendPacket(new TCEmblemStreamSendStatusPacket(ucc, (EmblemStreamStatus)0)); // 1?
                return;
            }
            
            _log.Warn("User {0} UCC {1}, sending part {2} ({3} => {4} / {5})", connection.GameConnection.ActiveChar.Name, customUcc.Id, index, startPos, endPos, customUcc.Data.Count);
            connection.SendPacket(new TCEmblemStreamDownloadPacket(ucc, index));
            
        }
        

        public void DownloadStatus(StreamConnection connection, ulong id, byte status, int count)
        {
            _log.Warn("DownloadStatus Id:{0}, Status: {1}, Count:{2}", id, status, count);
            if (!_uccs.ContainsKey(id))
                return;
            var ucc = _uccs[id];

            // status 4 == I'm ready to begin download of the image ?
            if ((status == 4) && (ucc is CustomUcc customUcc) && (customUcc.Data.Count > 0))
            {
                var maxParts = (int)Math.Ceiling((double)customUcc.Data.Count / TCEmblemStreamDownloadPacket.BufferSize);
                var sendPart = maxParts - count;
                if (sendPart > 0)
                {
                    RequestUccPart(connection, sendPart,0);
                }
                else
                {
                    RequestUcc(connection, id);
                }
            }
            // status 3 == I'm done downloading the image ?
            else if (status == 3)
            {
                connection.SendPacket(new TCEmblemStreamSendStatusPacket(ucc, EmblemStreamStatus.End));
            }
            else
            {
                connection.SendPacket(new TCEmblemStreamSendStatusPacket(ucc, EmblemStreamStatus.Start));
            }
        }

        public void ConfirmDefaultUcc(StreamConnection connection)
        {
            var ucc = _uploadQueue[connection.Id];
            var id = UccIdManager.Instance.GetNextId();

            ucc.Id = id;
            _uccs.Add(id, ucc);
            _uploadQueue.Remove(connection.Id);
            connection.SendPacket(new TCEmblemStreamRecvStatusPacket(EmblemStreamStatus.End));

            var character = connection.GameConnection.ActiveChar;

            connection.GameConnection.ActiveChar.ChangeMoney(SlotType.Inventory, -50000);
            
            var newItem = (UccItem)ItemManager.Instance.Create(17663, 1, 0, true);
            newItem.UccId = id;
            Save(ucc);
            character.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.GainItemWithUcc, newItem);
        }

        public void Save(Ucc ucc)
        {
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    ucc.Save(command);
                }
            }
        }
    }
}
