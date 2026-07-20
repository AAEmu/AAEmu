using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCastingStoppedPacket(ushort tlId, uint duration) : GamePacket(SCOffsets.SCCastingStoppedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tlId);
        stream.Write(duration);
        return stream;
    }
}
