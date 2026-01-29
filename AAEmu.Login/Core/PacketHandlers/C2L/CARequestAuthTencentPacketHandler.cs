using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestAuthTencentPacket"/> which is sent by the client to request authentication via
/// Tencent's services.
/// </summary>
public class CARequestAuthTencentPacketHandler : ILoginPacketHandler<CARequestAuthTencentPacket>
{
    public Task Execute(CARequestAuthTencentPacket packet, ILoginSession session,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
