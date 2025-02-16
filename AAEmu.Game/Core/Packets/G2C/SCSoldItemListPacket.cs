using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSoldItemListPacket : GamePacket
{
    private List<Item> _items;

    public SCSoldItemListPacket(List<Item> items) : base(SCOffsets.SCSoldItemListPacket, 5)
    {
        _items = new List<Item>();
        /*
        var startPos = items.Count < 12 ? 0 : items.Count - 12;
        var size = items.Count - startPos;
        _items.AddRange(items.GetRange(startPos, size));
        */
        _items.AddRange(items.GetRange(0, items.Count > 16 ? 16 : items.Count));
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_items.Count);
        foreach (var item in _items)
            stream.Write(item);
        return stream;
    }
}
