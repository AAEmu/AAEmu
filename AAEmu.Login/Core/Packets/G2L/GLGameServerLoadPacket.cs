using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

public class GLGameServerLoadPacket() : InternalPacket(GLOffsets.GLGameServerLoadPacket)
{
    public GSLoad Load { get; private set; }

    public override void Read(PacketStream stream)
    {
        Load = (GSLoad)stream.ReadByte();
    }
}
