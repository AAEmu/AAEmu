using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.G2L;

namespace AAEmu.Login.Core.PacketHandlers.G2L;

public class GLGameServerLoadPacketHandler()
    : IInternalPacketHandler<GLGameServerLoadPacket>
{
    public void Execute(GLGameServerLoadPacket packet, InternalConnection connection)
    {
        connection.GameServer!.Load = packet.Load;
    }
}
