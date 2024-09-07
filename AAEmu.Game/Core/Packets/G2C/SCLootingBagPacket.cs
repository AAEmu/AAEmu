using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

class SCLootingBagPacket : GamePacket
{
    private readonly List<Item> _items;
    private readonly bool _lootAll;
    private readonly bool _autoLoot;

    public SCLootingBagPacket(List<Item> items, bool lootAll, bool autoLoot) : base(SCOffsets.SCLootingBagPacket, 5)
    {
        _items = items;
        _lootAll = lootAll;
        _autoLoot = autoLoot;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_items.Count);

        foreach (var item in _items)
        {
            stream.Write(item);
        }
        stream.Write(_lootAll);
        stream.Write(_autoLoot);

        return stream;
    }
}
