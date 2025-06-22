using System.Collections.Concurrent;
using System.Text;

using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Models;
using NLog;

namespace AAEmu.Login.Core.Network.Login;

public class LoginProtocolHandler(
    IEnumerable<ILoginPacketDescriptor> packetDescriptors,
    ILoginConnectionTable loginConnectionTable) : BaseProtocolHandler, ILoginProtocolHandler
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<ushort, ILoginPacketDescriptor> _packets =
        new(packetDescriptors.ToDictionary(d => d.TypeId));

    public override void OnConnect(ISession session)
    {
        Logger.Debug($"Connection from {session.Ip} established, session id: {session.SessionId}");
        try
        {
            var con = new LoginConnection(session);
            LoginConnection.OnConnect();
            loginConnectionTable.AddConnection(con);
        }
        catch (Exception e)
        {
            session.Close();
            Logger.Error(e);
        }
    }

    public override void OnDisconnect(ISession session)
    {
        if (session is null)
        {
            Logger.Error("Unexpected null Session");
            return;
        }

        try
        {
            var con = loginConnectionTable.GetConnection(new ConnectionId(session.SessionId));
            if (con != null)
                loginConnectionTable.RemoveConnection(new ConnectionId(session.SessionId));
        }
        catch (Exception e)
        {
            session.Close();
            Logger.Error(e);
        }

        Logger.Debug($"Client from {session.Ip} disconnected");
    }

    public override void OnReceive(ISession session, byte[] buf, int offset, int bytes)
    {
        try
        {
            var connection = loginConnectionTable.GetConnection(new ConnectionId(session.SessionId));
            if (connection == null)
                return;
            OnReceive(connection, buf, offset, bytes);
        }
        catch (Exception e)
        {
            session.Close();
            Logger.Error(e);
        }
    }

    public void OnReceive(LoginConnection connection, byte[] buf, int offset, int bytes)
    {
        try
        {
            var stream = new PacketStream();
            if (connection.LastPacket != null)
            {
                stream.Insert(0, connection.LastPacket);
                connection.LastPacket = null;
            }

            stream.Insert(stream.Count, buf, 0, bytes);
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
                    var type = stream2.ReadUInt16();
                    if (!_packets.TryGetValue(type, out var packetDescriptor))
                    {
                        HandleUnknownPacket(connection, type, stream2);
                    }
                    else
                    {
                        try
                        {
                            packetDescriptor.Dispatch(stream2, connection);
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "Error on packet dispatch {0}", type);
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
            connection.Shutdown();
            Logger.Error(e);
        }
    }

    private static void HandleUnknownPacket(LoginConnection connection, uint type, PacketStream stream)
    {
        var dump = new StringBuilder();
        for (var i = stream.Pos; i < stream.Count; i++)
            dump.Append($"{stream.Buffer[i]:x2} ");
        Logger.Error("Unknown packet 0x{0:x2} from {1}:\n{2}", (object)type, (object)connection.Ip, (object)dump);
    }
}
