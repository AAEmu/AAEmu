using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLoginCharInfoHouse(uint id, House house) : GamePacket(SCOffsets.SCLoginCharInfoHousePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        return house.Write(stream);
    }
}
