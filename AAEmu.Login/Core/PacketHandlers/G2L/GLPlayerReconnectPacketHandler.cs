using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.G2L;

namespace AAEmu.Login.Core.PacketHandlers.G2L;

/// <summary>
/// Handles the <see cref="GLPlayerReconnectPacket"/> which is sent by the game server when a player attempts to
/// reconnect.
/// </summary>
public class GLPlayerReconnectPacketHandler(ILoginController loginController)
    : IInternalPacketHandler<GLPlayerReconnectPacket>
{
    public void Execute(GLPlayerReconnectPacket packet, InternalConnection connection)
    {
        loginController.AddReconnectionToken(connection, packet.GsId, packet.AccountId, packet.Token);
    }
}
