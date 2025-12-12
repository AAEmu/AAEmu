using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Login;

namespace AAEmu.Game.Core.Packets.G2L;

public class GLPlayerEnterPacket(uint connectionId, byte gsId, byte result) : LoginPacket(GLOffsets.GLPlayerEnterPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(connectionId);
        stream.Write(gsId);
        stream.Write(result);
        return stream;
    }
}
