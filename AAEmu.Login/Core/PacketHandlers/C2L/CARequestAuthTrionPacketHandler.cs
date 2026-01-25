using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestAuthTrionPacket"/> which is sent by the client to request authentication using
/// Trion credentials.
/// </summary>
public class CARequestAuthTrionPacketHandler(ILoginController loginController)
    : ILoginPacketHandler<CARequestAuthTrionPacket>
{
    public void Execute(CARequestAuthTrionPacket packet, LoginConnection connection)
    {
        var token = Helpers.StringToByteArray(packet.Password!);
        loginController.Login(connection, packet.Username!, token);
    }
}
