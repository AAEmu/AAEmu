using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using NLog;

namespace AAEmu.Game.Core.Network.Game;

public class GameProtocolHandler : BaseProtocolHandler
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>> _packets;

    public GameProtocolHandler()
    {
        _packets = new ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>>();
        _packets.TryAdd(1, new ConcurrentDictionary<uint, Type>()); // ordinary
        _packets.TryAdd(2, new ConcurrentDictionary<uint, Type>()); // proxy
        _packets.TryAdd(3, new ConcurrentDictionary<uint, Type>()); // deflate
        _packets.TryAdd(4, new ConcurrentDictionary<uint, Type>()); // deflate
        _packets.TryAdd(5, new ConcurrentDictionary<uint, Type>()); // encrypt
        _packets.TryAdd(6, new ConcurrentDictionary<uint, Type>()); // encrypt
    }

    public override void OnConnect(ISession session)
    {
        Logger.Info("Connect from {0} established, session id: {1}", session.Ip.ToString(), session.SessionId.ToString());
        try
        {
            var con = new GameConnection(session);
            GameConnection.OnConnect();
            GameConnectionTable.Instance.AddConnection(con);
        }
        catch (Exception e)
        {
            session.Close();
            Logger.Error(e);
        }
    }

    public override void OnDisconnect(ISession session)
    {
        try
        {
            var con = GameConnectionTable.Instance.GetConnection(session.SessionId);
            if (con != null)
            {
                if (con.ActiveChar != null)
                {
                    // On crash, force people out of the chat channels so we don't get phantom or duplicates
                    Managers.ChatManager.Instance.LeaveAllChannels(con.ActiveChar);
                    // ObjectIdManager.Instance.ReleaseId(con.ActiveChar.BcId);
                }
                con.OnDisconnect();
                StreamManager.Instance.RemoveToken(con.Id);
                GameConnectionTable.Instance.RemoveConnection(session.SessionId);
            }
            else
            {
                Logger.Error($"{nameof(OnDisconnect)}: connection for session id {session.SessionId} is null");
            }
        }
        catch (Exception e)
        {
            session.Close();
            Logger.Error(e);
        }

        Logger.Info("Client from {0} disconnected", session.Ip.ToString());
    }

    public override void OnReceive(ISession session, byte[] buf, int offset, int bytes)
    {
        try
        {
            var connection = GameConnectionTable.Instance.GetConnection(session.SessionId);
            if (connection == null)
            {
                Logger.Error($"{nameof(OnReceive)}: connection for session id {session.SessionId} is null");
                return;
            }

            OnReceive(connection, buf, offset, bytes);
        }
        catch (Exception e)
        {
            session.Close();
            Logger.Error(e);
        }
    }

    public void OnReceive(GameConnection connection, byte[] buf, int offset, int bytes)
    {
        try
        {
            var stream = new PacketStream();
            if (connection.LastPacket != null)
            {
                stream.Insert(0, connection.LastPacket);
                connection.LastPacket = null;
            }
            stream.Insert(stream.Count, buf, offset, bytes);
            while (stream != null && stream.Count > 0)
            {
                ushort len;
                try
                {
                    len = stream.ReadUInt16();
                }
                catch (MarshalException)
                {
                    //Logger.Warn("Error on reading type {0}", type);
                    stream.Rollback();
                    connection.LastPacket = stream;
                    stream = null;
                    continue;
                }
                var packetLen = len + stream.Pos;
                if (packetLen <= stream.Count)
                {
                    stream.Rollback();
                    var stream2 = new PacketStream();
                    stream2.Replace(stream, 0, packetLen);
                    if (stream.Count > packetLen)
                    {
                        var stream3 = new PacketStream();
                        stream3.Replace(stream, packetLen, stream.Count - packetLen);
                        stream = stream3;
                    }
                    else
                        stream = null;
                    stream2.ReadUInt16(); //len
                    stream2.ReadByte(); //unk
                    var level = stream2.ReadByte();

                    byte crc = 0;
                    byte counter = 0;
                    if (level == 1)
                    {
                        crc = stream2.ReadByte(); // TODO 1.2 crc
                        counter = stream2.ReadByte(); // TODO 1.2 counter
                    }
                    if (level == 5)
                    {
                        // packet from the client, decrypt
                        //------------------------------
                        var input = new byte[stream2.Count - 2];
                        Buffer.BlockCopy(stream2, 2, input, 0, stream2.Count - 2);
                        var output = EncryptionManager.Instance.Decode(input, connection.Id, connection.AccountId);

                        //// расскоментируйте блок кода для записи xorKey, packetBody, aesKey, IV для моего OpcodeFinder`a
                        //var bodyCrc = new byte[output.Length];
                        //Buffer.BlockCopy(output, 1, bodyCrc, 0, output.Length - 1); //получим тело пакета для подсчета контрольной суммы
                        //var crc8 = Crc8(bodyCrc); //посчитали CRC пакета

                        //using (StreamWriter writer = new StreamWriter("o:/inputCrc.txt", true))
                        //{
                        //    //writer.WriteLine("packetBodyCrypt:");
                        //    //writer.WriteLine(Helpers.ByteArrayToString(input));
                        //    writer.WriteLine("packetCrcBody:");
                        //    writer.WriteLine(Helpers.ByteArrayToString(output));
                        //}

                        var OutBytes = new byte[output.Length + 5];
                        Buffer.BlockCopy(stream2, 0, OutBytes, 0, 5);
                        // create a complete decrypted packet
                        Buffer.BlockCopy(output, 1, OutBytes, 5, output.Length - 1);
                        // replace encrypted data with decrypted ones
                        var strm = new PacketStream();
                        strm.Write(OutBytes);
                        stream2.Replace(strm, 0, OutBytes.Length);
                        stream2.ReadUInt16();
                    }

                    var type = stream2.ReadUInt16();
                    _packets[level].TryGetValue(type, out var classType);
                    if (classType == null)
                    {
                        HandleUnknownPacket(connection, type, level, stream2);
                    }
                    else
                    {
                        var packet = (GamePacket)Activator.CreateInstance(classType);
                        packet.Level = level;
                        packet.Connection = connection;
                        packet.Decode(stream2);
                    }
                }
                else
                {
                    stream.Rollback();
                    connection.LastPacket = stream;
                    stream = null;
                }
            }
        }
        catch (Exception e)
        {
            connection?.Shutdown();
            Logger.Error(e);
        }
    }

    public void RegisterPacket(uint type, byte level, Type classType)
    {
        _packets[level][type] = classType;
    }

    private static void HandleUnknownPacket(GameConnection connection, uint type, byte level, PacketStream stream)
    {
        var dump = new StringBuilder();
        for (var i = stream.Pos; i < stream.Count; i++)
            dump.AppendFormat("{0:x2} ", stream.Buffer[i]);
        Logger.Error("Unknown packet 0x{0:x2}({3}) from {1}:\n{2}", type, connection.Ip, dump, level);
    }

    //Methods for SC packet Encryption
    /// <summary>
    /// Подсчет контрольной суммы пакета, используется в шифровании пакетов DD05 и 0005
    /// </summary>
    /// <param name="data"></param>
    /// <param name="size"></param>
    /// <returns>Crc8</returns>
    private byte Crc8(byte[] data, int size)
    {
        uint checksum = 0;
        for (var i = 0; i < size; i++)
        {
            checksum *= 0x13;
            checksum += data[i];
        }
        return (byte)checksum;
    }

    public byte Crc8(byte[] data)
    {
        return Crc8(data, data.Length);
    }

}
