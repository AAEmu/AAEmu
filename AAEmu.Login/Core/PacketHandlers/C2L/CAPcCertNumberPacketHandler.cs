using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CAPcCertNumberPacket"/> which is sent by the client to provide the certificate number
/// for Korean authentication.
/// </summary>
public class CAPcCertNumberPacketHandler : ILoginPacketHandler<CAPcCertNumberPacket>
{
    public async Task Execute(CAPcCertNumberPacket packet, ILoginSession session, CancellationToken cancellationToken)
    {
        await session.ContinueAuthAsync<ICertAuthFlow>(
            flow => flow.SubmitCertAsync(session.Client, packet.CertNumber ?? "", cancellationToken), cancellationToken);
    }
}
