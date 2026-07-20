using System.Collections.Concurrent;
using System.Text;

using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;

using NLog;

namespace AAEmu.Game.Core.Network.Game;

public class GameProtocolHandler : BaseProtocolHandler
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// List of packet handlers (level, id, class type)
    /// </summary>
    private readonly ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>> _packets;

    public GameProtocolHandler()
    {
        _packets = new ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>>();
        // For 1.2 client we only have Level1 and Level2 packets
        _packets.TryAdd(1, new ConcurrentDictionary<uint, Type>());
        _packets.TryAdd(2, new ConcurrentDictionary<uint, Type>());
        // Level 5/6 for encrypted packets (3.5)
        _packets.TryAdd(3, new ConcurrentDictionary<uint, Type>());
        _packets.TryAdd(4, new ConcurrentDictionary<uint, Type>());
        _packets.TryAdd(5, new ConcurrentDictionary<uint, Type>());
        _packets.TryAdd(6, new ConcurrentDictionary<uint, Type>());
        EncryptionManager.needNewkey1 = false;
        EncryptionManager.needNewkey2 = false;
    }

    /// <summary>
    /// On connect event
    /// </summary>
    /// <param name="session"></param>
    public override void OnConnect(ISession session)
    {
        Logger.Info($"Connect from {session.Ip} established, session id: {session.SessionId}");
        try
        {
            var con = new GameConnection(session);
            con.OnConnect();
            GameConnectionTable.Instance.AddConnection(con);
        }
        catch (Exception e)
        {
            session.Close();
            Logger.Error(e);
        }
    }

    /// <summary>
    /// On Disconnect event
    /// </summary>
    /// <param name="session"></param>
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

        Logger.Info($"Client from {session.Ip} disconnected");
    }

    /// <summary>
    /// Handle incoming data for session
    /// </summary>
    /// <param name="session"></param>
    /// <param name="buf"></param>
    /// <param name="offset"></param>
    /// <param name="bytes"></param>
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

    /// <summary>
    /// Handle incoming data for GameConnection
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="buf"></param>
    /// <param name="offset"></param>
    /// <param name="bytes"></param>
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
            while (stream is { Count: > 0 })
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
                    EncryptionManager.needNewkey1 = true;
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

                    //byte crc = 0;
                    //byte counter = 0;
                    byte[] encryptedInput = null;
                    if (level == 1)
                    {
                        _ = stream2.ReadByte(); // TODO: verify 1.2 crc
                        _ = stream2.ReadByte(); // TODO: verify 1.2 counter
                    }
                    if (level == 5)
                    {
                        // packet from the client, decrypt
                        //------------------------------
                        var input = new byte[stream2.Count - 2];
                        Buffer.BlockCopy(stream2, 2, input, 0, stream2.Count - 2);
                        encryptedInput = input;
                        var output = EncryptionManager.Instance.Decode(input, connection.Id, connection.AccountId);
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
                        packet!.Level = level;
                        packet.Connection = connection;
                        if (Logger.IsDebugEnabled && type == Packets.C2G.CSOffsets.CSCreateCharacterPacket)
                        {
                            var raw = new PacketStream().Replace(stream2, stream2.Pos, stream2.Count - stream2.Pos).GetBytes();
                            Logger.Debug($"Raw CSCreateCharacterPacket plaintext ({raw.Length} bytes): {Convert.ToHexString(raw)}");
                            if (encryptedInput != null)
                                Logger.Debug($"Raw CSCreateCharacterPacket ciphertext ({encryptedInput.Length} bytes): {Convert.ToHexString(encryptedInput)}");
                        }
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

    /// <summary>
    /// Registers a GamePacket handler by Id and Level
    /// </summary>
    /// <param name="type"></param>
    /// <param name="level"></param>
    /// <param name="classType"></param>
    public void RegisterPacket(uint type, byte level, Type classType)
    {
        _packets[level][type] = classType;
    }

    /// <summary>
    /// Handle and Log unknown packet data
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="type"></param>
    /// <param name="level"></param>
    /// <param name="stream"></param>
    private static void HandleUnknownPacket(GameConnection connection, uint type, byte level, PacketStream stream)
    {
        var dump = new StringBuilder();
        for (var i = stream.Pos; i < stream.Count; i++)
            dump.AppendFormat("{0:x2} ", stream.Buffer[i]);
        Logger.Error($"Unknown packet 0x{type:x2}({level}) from {connection.Ip}:\n{dump}");
        if (type > 0x1ff)
        {
            EncryptionManager.needNewkey1 = true;
        }
    }
}
