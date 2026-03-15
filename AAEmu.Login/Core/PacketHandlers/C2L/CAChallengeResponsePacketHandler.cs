using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CAChallengeResponsePacket"/> (V1 Korea challenge response).
/// </summary>
/// <seealso cref="AAEmu.Login.Core.Packets.L2C.ACChallengePacket"/>
public class CAChallengeResponsePacketHandler : ILoginPacketHandler<CAChallengeResponsePacket>
{
    public async Task Execute(CAChallengeResponsePacket packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        await session.ContinueAuthAsync<IChallengeAuthFlow>(
            flow => flow.ContinueAsync(session.Client, packet.Ch, packet.Pw, cancellationToken),
            cancellationToken);
    }
}
