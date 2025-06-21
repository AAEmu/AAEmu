using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

public class CARequestAuthPacketHandler(ILoginController loginController)
    : ILoginPacketHandler<CARequestAuthPacket>
{
    public void Execute(CARequestAuthPacket packet, LoginConnection connection)
    {
        loginController.Login(connection, packet.Account!);

        // Connection.SendPacket(new ACChallengePacket()); // TODO ...
    }
}
