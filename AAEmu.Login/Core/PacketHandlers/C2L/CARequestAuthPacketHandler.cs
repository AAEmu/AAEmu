using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestAuthPacket"/> which is sent by the client to request authentication.
/// </summary>
public class CARequestAuthPacketHandler(ILoginController loginController) : ILoginPacketHandler<CARequestAuthPacket>
{
    public async Task Execute(CARequestAuthPacket packet, ILoginConnection connection,
        CancellationToken cancellationToken)
    {
        await loginController.Login(connection, packet.Account!);

        // Connection.SendPacket(new ACChallengePacket()); // TODO ...
    }
}
