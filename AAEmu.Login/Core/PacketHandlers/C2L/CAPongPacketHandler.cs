using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;
using Microsoft.Extensions.Logging;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CAPongPacket"/> heartbeat reply sent by the client in response to an
/// <see cref="AAEmu.Login.Core.Packets.L2C.ACPingPacket"/>.
/// </summary>
public class CAPongPacketHandler(ILogger<CAPongPacketHandler> logger) : ILoginPacketHandler<CAPongPacket>
{
    public Task Execute(CAPongPacket packet, ILoginSession session, CancellationToken cancellationToken)
    {
        logger.LogTrace("Auth pong received (echo {Echo})", packet.Send);
        return Task.CompletedTask;
    }
}