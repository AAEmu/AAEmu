using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestAuthTencentPacket"/> authentication packet.
/// </summary>
public class CARequestAuthTencentPacketHandler(IKoreaAuthFlowFactory authFlowFactory)
    : ILoginPacketHandler<CARequestAuthTencentPacket>
{
    public Task Execute(CARequestAuthTencentPacket packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        var flow = authFlowFactory.Create(packet.Account!, session.Connection.Ip);
        return session.AuthenticateAsync(flow, cancellationToken);
    }
}
