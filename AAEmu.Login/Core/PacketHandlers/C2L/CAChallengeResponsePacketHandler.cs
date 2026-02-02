using System.Text;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CAChallengeResponsePacket"/> which is sent by the client in response to a challenge made by
/// the login server.
/// </summary>
/// <seealso cref="AAEmu.Login.Core.Packets.L2C.ACChallengePacket"/>
public class CAChallengeResponsePacketHandler : ILoginPacketHandler<CAChallengeResponsePacket>
{
    public async Task Execute(CAChallengeResponsePacket packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        var password = Encoding.UTF8.GetString(packet.Password!);
        await session.ContinueAuthAsync<IChallengeAuthFlow>(
            flow => flow.ContinueAsync(session.Client, password, cancellationToken), cancellationToken);
    }
}
