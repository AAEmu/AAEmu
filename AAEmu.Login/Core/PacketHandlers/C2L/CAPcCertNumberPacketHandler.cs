using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CAPcCertNumberPacket"/> which is sent by the client to provide a certificate number.
/// </summary>
public class CAPcCertNumberPacketHandler : ILoginPacketHandler<CAPcCertNumberPacket>
{
    public Task Execute(CAPcCertNumberPacket packet, ILoginSession session,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
