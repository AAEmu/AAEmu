using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestAuthPacket"/> which is sent by the client to request authentication.
/// </summary>
public class CARequestAuthPacketHandler(IKoreaAuthFlowFactory authFlowFactory)
    : ILoginPacketHandler<CARequestAuthPacket>
{
    public async Task Execute(CARequestAuthPacket packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        var flow = authFlowFactory.Create(packet.Account!, session.Connection.Ip);
        await session.AuthenticateAsync(flow, cancellationToken);
    }
}
