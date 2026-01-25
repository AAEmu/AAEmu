using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestAuthMailRuPacket"/> which is sent by the client to request authentication via
/// Mail.Ru.
/// </summary>
public class CARequestAuthMailRuPacketHandler(ILoginController loginController)
    : ILoginPacketHandler<CARequestAuthMailRuPacket>
{
    public void Execute(CARequestAuthMailRuPacket packet, LoginConnection connection)
    {
        loginController.Login(connection, packet.Id!, packet.Token);
    }
}
