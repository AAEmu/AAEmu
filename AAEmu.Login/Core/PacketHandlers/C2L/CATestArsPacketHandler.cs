using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;
using Microsoft.Extensions.Logging;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CATestArsPacket"/> developer/test packet. ARS verification is callback-based
/// (the external phone system calls back to the server), so there is nothing to drive from this packet —
/// it is logged for diagnostics only.
/// </summary>
public class CATestArsPacketHandler(ILogger<CATestArsPacketHandler> logger) : ILoginPacketHandler<CATestArsPacket>
{
    public Task Execute(CATestArsPacket packet, ILoginSession session, CancellationToken cancellationToken)
    {
        logger.LogDebug("ARS test packet received (number {Number})", packet.Number);
        return Task.CompletedTask;
    }
}