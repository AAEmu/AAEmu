using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Core.Packets.G2L;

public class GLGameServerLoadPacket() : InternalPacket(GLOffsets.GLGameServerLoadPacket)
{
    public override void Read(PacketStream stream)
    {
        Connection.GameServer.SetLoad(stream.ReadByte());
    }
}
