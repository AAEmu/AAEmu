using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.G2L;

namespace AAEmu.Login.Core.PacketHandlers.G2L;

/// <summary>
/// Handles the <see cref="GLGameServerLoadPacket"/> to update the load of a game server.
/// </summary>
public class GLGameServerLoadPacketHandler : IInternalPacketHandler<GLGameServerLoadPacket>
{
    public Task Execute(GLGameServerLoadPacket packet, InternalConnection connection)
    {
        connection.GameServer!.Load = packet.Load;
        return Task.CompletedTask;
    }
}
