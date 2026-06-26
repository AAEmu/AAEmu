using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;
using Microsoft.Extensions.Logging;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestAuthPWDPacket"/> carrying a password submission. The current Korea auth flows
/// are challenge/OTP/cert based and do not expose a standalone password-continuation step, so this packet is
/// logged for diagnostics.
/// </summary>
public class CARequestAuthPWDPacketHandler(ILogger<CARequestAuthPWDPacketHandler> logger)
    : ILoginPacketHandler<CARequestAuthPWDPacket>
{
    public Task Execute(CARequestAuthPWDPacket packet, ILoginSession session, CancellationToken cancellationToken)
    {
        logger.LogDebug("Password auth packet received");
        return Task.CompletedTask;
    }
}