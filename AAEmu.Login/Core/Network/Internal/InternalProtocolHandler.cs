using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Models;
using NLog;

namespace AAEmu.Login.Core.Network.Internal;

public class InternalProtocolHandler(
    IEnumerable<IInternalPacketDescriptor> packetDescriptors,
    IGameController gameController,
    IInternalConnectionTable internalConnectionTable)
    : BaseProtocolHandler, IInternalProtocolHandler
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<ushort, IInternalPacketDescriptor> _packets =
        new(packetDescriptors.ToDictionary(d => d.TypeId));

    public override void OnConnect(ISession session)
    {
        Logger.Info("GameServer from {0} connected, session id: {1}", session.Ip.ToString(),
            session.SessionId.ToString(CultureInfo.InvariantCulture));
        var con = new InternalConnection(session);
        InternalConnection.OnConnect();
        internalConnectionTable.AddConnection(con);
    }

    public override void OnDisconnect(ISession session)
    {
        Logger.Info("GameServer from {0} disconnected", session.Ip.ToString());
        if (session.GetAttribute("gsId") is { } gsId)
            gameController.Remove((GameServerId)gsId);
        internalConnectionTable.RemoveConnection(session.SessionId);
    }

    public override void OnReceive(ISession session, byte[] buf, int offset, int bytes)
    {
        var connection = internalConnectionTable.GetConnection(session.SessionId);
        if (connection == null)
        {
            Logger.Error("Connection not found for session {0}", session.SessionId);
            return;
        }

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

                stream2.ReadUInt16();
                var type = stream2.ReadUInt16();
                if (!_packets.TryGetValue(type, out var packetDescriptor))
                {
                    HandleUnknownPacket(session, type, stream2);
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

    private static void HandleUnknownPacket(ISession session, uint type, PacketStream stream)
    {
        var dump = new StringBuilder();
        for (var i = stream.Pos; i < stream.Count; i++)
            dump.Append($"{stream.Buffer[i]:x2} ");
        Logger.Error("Unknown packet 0x{0:x2} from {1}:\n{2}", type, session.Ip, dump);
    }
}
