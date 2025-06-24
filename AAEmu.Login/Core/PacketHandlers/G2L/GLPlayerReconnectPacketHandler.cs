using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.G2L;

namespace AAEmu.Login.Core.PacketHandlers.G2L;

public class GLPlayerReconnectPacketHandler(ILoginController loginController)
    : IInternalPacketHandler<GLPlayerReconnectPacket>
{
    public Task ExecuteAsync(GLPlayerReconnectPacket packet, InternalConnection connection)
    {
        loginController.AddReconnectionToken(connection, packet.GsId, packet.AccountId, packet.Token);
        return Task.CompletedTask;
    }
}
