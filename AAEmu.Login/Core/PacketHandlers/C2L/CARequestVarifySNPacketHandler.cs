using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;
using Microsoft.Extensions.Logging;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestVarifySNPacket"/> carrying a security-card number. No security-card auth flow
/// is wired up server-side, so the submission is logged for diagnostics.
/// </summary>
public class CARequestVarifySNPacketHandler(ILogger<CARequestVarifySNPacketHandler> logger)
    : ILoginPacketHandler<CARequestVarifySNPacket>
{
    public Task Execute(CARequestVarifySNPacket packet, ILoginSession session, CancellationToken cancellationToken)
    {
        logger.LogDebug("Security card number received");
        return Task.CompletedTask;
    }
}