using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseFarmPacket(string name, int total, int harvestable) : GamePacket(SCOffsets.SCHouseFarmPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(name);
        stream.Write(total);
        stream.Write(harvestable);
        return stream;
    }
}
