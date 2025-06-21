using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

public class CARequestReconnectPacketHandler(ILoginController loginController)
    : ILoginPacketHandler<CARequestReconnectPacket>
{
    public void Execute(CARequestReconnectPacket packet, LoginConnection connection)
    {
        loginController.Reconnect(connection, packet.GsId, packet.AccountId, packet.Cookie);
    }
}
