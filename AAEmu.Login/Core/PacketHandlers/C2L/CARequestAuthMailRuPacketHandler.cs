using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

public class CARequestAuthMailRuPacketHandler(ILoginController loginController)
    : ILoginPacketHandler<CARequestAuthMailRuPacket>
{
    public Task ExecuteAsync(CARequestAuthMailRuPacket packet, LoginConnection connection)
    {
        loginController.LoginAsync(connection, packet.Id!, packet.Token);
        return Task.CompletedTask;
    }
}
