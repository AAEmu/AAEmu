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
    /// On disconnect event
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
                    var bodyStream = stream2;
                    byte lookupLevel = level;
                    ushort type;

                    if (level == 5)
                    {
                        // Encrypted C->S frame: [len][unk][level=5][hash][AES cipher]. Decrypt to
                        // [count][type u16][body]; the decrypted packet is a normal level-1 game packet.
                        var input = new byte[packetLen - 2];
                        Array.Copy(stream2.Buffer, 2, input, 0, packetLen - 2);
                        var plain = EncryptionManager.Instance.CSDecrypt(input, connection.AccountId, connection.Id);
                        if (plain == null || plain.Length < 3)
                        {
                            Logger.Warn("C2S level-5 decrypt unavailable (len={0}, decrypted={1})", packetLen, plain?.Length ?? -1);
                            continue;
                        }
                        // Decrypted plaintext = [crc8][count][type u16][body] (same header shape as S->C).
                        bodyStream = new PacketStream();
                        bodyStream.Insert(0, plain, 0, plain.Length);
                        bodyStream.ReadByte();          // crc8
                        bodyStream.ReadByte();          // count (CSMessageCount)
                        type = bodyStream.ReadUInt16(); // real 10.0.2.13 opcode
                        lookupLevel = 1;                // decrypted game packets dispatch as level-1 CS packets
                    }
                    else
                    {
                        if (level == 1)
                        {
                            _ = stream2.ReadByte(); // TODO: verify 1.2 crc
                            _ = stream2.ReadByte(); // TODO: verify 1.2 counter
                        }
                        type = stream2.ReadUInt16();
                    }

                    // Guard the level lookup (only 1 and 2 are registered); unknown levels fall through to
                    // HandleUnknownPacket rather than throwing KeyNotFoundException up to the outer catch
                    // (which would Shutdown the connection).
                    Type classType = null;
                    if (_packets.TryGetValue(lookupLevel, out var levelMap))
                        levelMap.TryGetValue(type, out classType);
                    // DEBUG: log the raw C2S opcode (now incl. the decrypted level-5 opcodes — these reveal
                    // the real 10.0.2.13 CS values to remap CSOffsets). Skip the level-2 keepalive noise.
                    if (level != 2 || (type != 0x012 && type != 0x013 && type != 0x015 && type != 0x016))
                        Logger.Warn("C2S RAW opcode=0x{0:X3} level={1} -> {2}", type, level, classType?.Name ?? "UNKNOWN");
                    if (classType == null)
                    {
                        HandleUnknownPacket(connection, type, level, bodyStream);
                    }
                    else
                    {
                        // DEBUG: don't let a mis-dispatched C2S packet (wrong CSOffsets) kill the connection,
                        // so we can observe the full sequence of raw opcodes. The outer framing is length-based
                        // so per-packet failures don't desync. Remove once CSOffsets is remapped.
                        try
                        {
                            var packet = (GamePacket)Activator.CreateInstance(classType);
                            packet!.Level = level;
                            packet.Connection = connection;
                            packet.Decode(bodyStream);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn("C2S dispatch of 0x{0:X3} ({1}) threw: {2}", type, classType.Name, ex.Message);
                        }
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
    }
}
