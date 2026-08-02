using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCOtherTradeItemTookdownPacket(Item item, int amount) : GamePacket(SCOffsets.SCOtherTradeItemTookdownPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        item.Write(stream, amount);
        return stream;
    }
}
