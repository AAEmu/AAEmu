using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Login;

namespace AAEmu.Game.Core.Packets.G2L;

public class GLGameServerLoadPacket(byte load) : LoginPacket(GLOffsets.GLGameServerLoadPacket)
{
    public byte Load { get; set; } = load;

    public override PacketStream Write(PacketStream stream)
    {
        
        stream.Write(Load);
        return stream;
    }
}
