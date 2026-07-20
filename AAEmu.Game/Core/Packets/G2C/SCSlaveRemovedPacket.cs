using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSlaveRemovedPacket(uint id, ushort tl) : GamePacket(SCOffsets.SCSlaveRemovedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(id);
        stream.Write(tl);
        return stream;
    }
}
