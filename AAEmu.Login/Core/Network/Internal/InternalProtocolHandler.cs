using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
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

        PacketStream? stream = new PacketStream();
        if (connection.LastPacket != null)
        {
            stream.Insert(0, connection.LastPacket);
            connection.LastPacket = null;
        }

        stream.Insert(stream.Count, buf, offset, bytes);
        while (stream is { Count: > 0 })
        {
            switch (LengthPrefixedFrames.TryTake(ref stream, LengthPrefixedFrames.MinOpcodePayloadBytes, out var frame))
            {
                case LengthPrefixedFrameResult.NeedMore:
                    connection.LastPacket = stream;
                    return;
                case LengthPrefixedFrameResult.DroppedInvalidLength:
                    logger.LogWarning("Dropped invalid internal frame from {IP}", session.Ip);
                    continue;
                case LengthPrefixedFrameResult.GotFrame:
                    frame!.ReadUInt16();
                    var type = frame.ReadUInt16();
                    if (!_packets.TryGetValue(type, out var packetDescriptor))
                    {
                        HandleUnknownPacket(session, type, frame);
                    }
                    else
                    {
                        try
                        {
                            packetDescriptor.Dispatch(frame, connection);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error on packet dispatch {Type}", type);
                        }
                    }

                    break;
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
