using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLootBagDataPacket(List<Item> items, bool lootAll) : GamePacket(SCOffsets.SCLootBagDataPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)items.Count);

        foreach (var item in items)
            item.Write(stream);

        stream.Write(lootAll);
        return stream;
    }
}
