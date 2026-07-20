using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMyHousePacket(House house) : GamePacket(SCOffsets.SCMyHousePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        return house.Write(stream);
    }
}
