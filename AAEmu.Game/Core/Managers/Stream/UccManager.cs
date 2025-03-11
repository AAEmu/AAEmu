using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.S2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Stream;
using NLog;

// If you want your server to store the UCC data as actual files instead of in the MediumBlob field, you can enable the
// below define by removing the comment before it. Please note that existing data will not migrate if this setting is
// changed on a already existing server. (recommended to keep OFF)
//#define STORE_UCC_AS_FILE

namespace AAEmu.Game.Core.Managers.Stream;

public class UccManager : Singleton<UccManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private Dictionary<uint, Ucc> _uploadQueue;
    private Dictionary<uint, UccUploadHandle> _complexUploadParts;
    private Dictionary<ulong, Ucc> _uccs;
    private Dictionary<uint, ulong> _downloadQueue; // connection, UCCId
    private static readonly object s_lockObject = new();

    /// <summary>
    /// Helper function to get the actual byte size of the data blob field
    /// </summary>
    /// <param name="uccId"></param>
    /// <returns></returns>
    private static long GetUccBlobSize(ulong uccId)
    {
        using (var connection = MySQL.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT OCTET_LENGTH(data) FROM uccs WHERE id = @id";
            command.Parameters.AddWithValue("@id", uccId);
            command.Prepare();
            var res = command.ExecuteScalar();
            if (res is long resVal)
                return resVal;
        }
        return 0;
    }

    public void Load()
    {
        _uploadQueue = new Dictionary<uint, Ucc>();
        _complexUploadParts = new Dictionary<uint, UccUploadHandle>();
        _uccs = new Dictionary<ulong, Ucc>();
        lock (s_lockObject)
        {
            _downloadQueue = new Dictionary<uint, ulong>();
        }

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
                                Type = type,
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
                        }
                        else if (type == UccType.Complex)
                        {
                            var ucc = new CustomUcc()
                            {
                                Id = id,
                                Type = type,
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

#if STORE_UCC_AS_FILE
                            
                            ucc.SaveDataInDB = false ;
                            // Read UCC data from disk
                            var uccFileName = Path.Combine(FileManager.AppPath, "UserData", "UCC", id.ToString("000000") + ".dds");
                            if (File.Exists(uccFileName))
                                ucc.Data.AddRange(File.ReadAllBytes(uccFileName));
                            else
                                Logger.Error("Missing UCC file: {0}", uccFileName);
                            
#else

                            ucc.SaveDataInDB = true;

                            // Read UCC data from DB
                            var blobSize = reader.IsDBNull(reader.GetOrdinal("data")) ? 0 : GetUccBlobSize(ucc.Id);
                            if (blobSize > 0)
                            {
                                var blobBuffer = new byte[blobSize];
                                var readData = reader.GetBytes(reader.GetOrdinal("data"), 0, blobBuffer, 0, (int)blobSize);
                                if (readData == blobSize)
                                {
                                    ucc.Data = blobBuffer.ToList();
                                }
                                else
                                {
                                    Logger.Error("Read blob data size did not match suggested size for UCC {0}", ucc.Id);
                                    ucc.Data.Clear();
                                }
                            }
                            else
                            {
                                Logger.Warn("CustomUcc has no data for UccId {0}", ucc.Id);
                            }

#endif

                            _uccs.Add(id, ucc);
                        }
                    }
                }
            }
        }
    }

    public void StartUpload(StreamConnection connection, int expectedDataSize, CustomUcc customUcc)
    {
        // Make sure the newly created customUcc has it's SaveDataInDB value set correctly
#if STORE_UCC_AS_FILE
        customUcc.SaveDataInDB = false;
#else
        customUcc.SaveDataInDB = true;
#endif

        _uploadQueue.Add(connection.Id, customUcc);
        var uploadHandler = new UccUploadHandle() { ExpectedSize = expectedDataSize, UploadingUcc = customUcc };
        _complexUploadParts.Add(connection.Id, uploadHandler);
        connection.SendPacket(new TCEmblemStreamRecvStatusPacket(EmblemStreamStatus.Continue));
    }

    public void UploadPart(StreamConnection connection, UccPart part)
    {
        if (!_complexUploadParts.TryGetValue(connection.Id, out var handle))
            return;

        handle.AddPart(part);

        if (handle.UploadComplete)
        {
            handle.FinalizeUpload();
            ConfirmDefaultUcc(connection);
        }
        else
        {
            connection.SendPacket(new TCEmblemStreamRecvStatusPacket(EmblemStreamStatus.Continue));
        }
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
        if (_uccs.TryGetValue(id, out var ucc))
            connection.SendPacket(new TCUccComplexPacket(ucc));
    }

    public void RequestUcc(StreamConnection connection, ulong id)
    {
        Logger.Warn("User {0} requesting UCC {1}", connection.GameConnection.ActiveChar.Name, id);
        if (!_uccs.TryGetValue(id, out var ucc))
            return;

        lock (s_lockObject)
        {

            if (_downloadQueue.TryGetValue(connection.Id, out var value))
                if (value == id)
                {
                    //Logger.Warn("User {0} is already requesting UCC {1}, skipping request !", connection.GameConnection.ActiveChar.Name, id);
                    Logger.Warn("User {0} is already requesting UCC {1}, sending first block !", connection.GameConnection.ActiveChar.Name, id);
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
        Logger.Warn("User {0} validated UCC part {1} ({2} bytes), sending next part", connection.GameConnection.ActiveChar.Name, previousIndex, previousSize);
        ulong uccId = 0;
        lock (s_lockObject)
        {
            if (!_downloadQueue.TryGetValue(connection.Id, out var uccIdVal))
            {
                Logger.Warn("User {0} UCC {1} was not in the request queue",
                    connection.GameConnection.ActiveChar.Name, previousIndex);
                return;
            }
            uccId = uccIdVal;
        }

        if (!_uccs.ContainsKey(uccId))
            return;

        var ucc = _uccs[uccId];
        if (!(ucc is CustomUcc customUcc))
            return;

        var index = previousIndex;
        index++;
        var startPos = index * TCEmblemStreamDownloadPacket.BufferSize;
        var endPos = startPos + TCEmblemStreamDownloadPacket.BufferSize;

        if (startPos >= customUcc.Data.Count)
        {
            lock (s_lockObject)
            {
                _downloadQueue.Remove(connection.Id);
            }
            Logger.Warn("User {0} UCC {1}, sending end part TCEmblemStreamSendStatusPacket", connection.GameConnection.ActiveChar.Name, customUcc.Id);
            connection.SendPacket(new TCEmblemStreamSendStatusPacket(ucc, 0)); // 1?
            return;
        }

        Logger.Warn("User {0} UCC {1}, sending part {2} ({3} => {4} / {5})", connection.GameConnection.ActiveChar.Name, customUcc.Id, index, startPos, endPos, customUcc.Data.Count);
        connection.SendPacket(new TCEmblemStreamDownloadPacket(ucc, index));

    }


    public void DownloadStatus(StreamConnection connection, ulong id, byte status, int count)
    {
        Logger.Warn("DownloadStatus Id:{0}, Status: {1}, Count:{2}", id, status, count);
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
                RequestUccPart(connection, sendPart, 0);
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

#if STORE_UCC_AS_FILE
        // Temporary on-disk storage of UCC data
        if ((ucc is CustomUcc customUcc) && (customUcc.Data.Count > 0))
        {
            var uccFileName = Path.Combine(FileManager.AppPath, "UserData", "UCC", id.ToString("000000") + ".dds");
            File.WriteAllBytes(uccFileName,customUcc.Data.ToArray());
            if (!File.Exists(uccFileName))
                Logger.Error("Failed to save UCC data to file {0}", uccFileName);
        }
#endif

        connection.SendPacket(new TCEmblemStreamRecvStatusPacket(EmblemStreamStatus.End));

        var character = connection.GameConnection.ActiveChar;

        connection.GameConnection.ActiveChar.ChangeMoney(SlotType.Bag, -50000);

        var newItem = (UccItem)ItemManager.Instance.Create(Item.CrestInk, 1, 0, true); // Crest Ink
        newItem.UccId = id;
        Save(ucc);
        character.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.CreateOriginUcc, newItem);
    }

    public static void CreateStamp(Character player, Item sourceInk)
    {
        var newItem = ItemManager.Instance.Create(Item.CrestStamp, 1, 0, true); // Crest Stamp
        newItem.UccId = sourceInk.UccId;
        player.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.CreateOriginUcc, newItem);
    }

    public static void ApplyStamp(Item stamp, Item targetItem)
    {
        targetItem.UccId = stamp.UccId;
    }

    public static void Save(Ucc ucc)
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

    public Ucc GetUccFromItem(Item item)
    {
        if (_uccs.TryGetValue(item.UccId, out var ucc))
            return ucc;

        return null;
    }
}
