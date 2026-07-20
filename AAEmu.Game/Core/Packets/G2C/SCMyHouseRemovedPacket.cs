using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMyHouseRemovedPacket(ushort tl) : GamePacket(SCOffsets.SCMyHouseRemovedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        return stream;
    }
}
