using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Models;
using ISession = AAEmu.Commons.Network.Core.ISession;

namespace AAEmu.Login.Core.Network.Internal;

public class InternalProtocolHandler(
    IEnumerable<IInternalPacketDescriptor> packetDescriptors,
    IGameController gameController,
    IInternalConnectionTable internalConnectionTable,
    ILogger<InternalProtocolHandler> logger)
    : BaseProtocolHandler, IInternalProtocolHandler
{
    private readonly ConcurrentDictionary<ushort, IInternalPacketDescriptor> _packets =
        new(packetDescriptors.ToDictionary(d => d.TypeId));

    public override void OnConnect(ISession session)
    {
        logger.LogInformation("GameServer from {GameServerIP} connected, session id: {SessionID}",
            session.Ip.ToString(), session.SessionId.ToString(CultureInfo.InvariantCulture));
        var con = new InternalConnection(session);
        InternalConnection.OnConnect();
        internalConnectionTable.AddConnection(con);
    }

    public override void OnDisconnect(ISession session)
    {
        logger.LogInformation("GameServer from {GameServerIP} disconnected", session.Ip.ToString());
        if (session.GetAttribute("gsId") is { } gsId)
            gameController.Remove((GameServerId)gsId);
        internalConnectionTable.RemoveConnection(session.SessionId);
    }

    public override void OnReceive(ISession session, byte[] buf, int offset, int bytes)
    {
        var connection = internalConnectionTable.GetConnection(session.SessionId);
        if (connection == null)
        {
            logger.LogError("Connection not found for session {SessionID}", session.SessionId);
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
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error on packet dispatch {Type}", type);
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

    private void HandleUnknownPacket(ISession session, uint type, PacketStream stream)
    {
        if (!logger.IsEnabled(LogLevel.Error))
        {
            return;
        }

        var dump = new StringBuilder();
        for (var i = stream.Pos; i < stream.Count; i++)
            dump.Append($"{stream.Buffer[i]:x2} ");
        logger.LogError("Unknown packet 0x{Type:x2} from {IP}:\n{Dump}", type, session.Ip, dump);
    }
}
