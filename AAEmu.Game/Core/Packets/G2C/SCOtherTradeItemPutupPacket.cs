using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCOtherTradeItemPutupPacket(Item item) : GamePacket(SCOffsets.SCOtherTradeItemPutupPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(item);
        return stream;
    }
}
