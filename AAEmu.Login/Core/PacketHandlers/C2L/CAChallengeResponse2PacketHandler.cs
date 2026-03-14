using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CAChallengeResponse2Packet"/> (V2 Korea challenge response).
/// </summary>
/// <seealso cref="AAEmu.Login.Core.Packets.L2C.ACChallenge2Packet"/>
public class CAChallengeResponse2PacketHandler : ILoginPacketHandler<CAChallengeResponse2Packet>
{
    public async Task Execute(CAChallengeResponse2Packet packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        await session.ContinueAuthAsync<IChallenge2AuthFlow>(
            flow => flow.ContinueV2Async(session.Client, packet.Ch, cancellationToken),
            cancellationToken);
    }
}
