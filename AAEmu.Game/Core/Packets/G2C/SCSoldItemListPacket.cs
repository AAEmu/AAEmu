using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSoldItemListPacket(List<Item> items) : GamePacket(SCOffsets.SCSoldItemListPacket, 5)
{
    private readonly List<Item> _items =
    [
        /*
        var startPos = items.Count < 12 ? 0 : items.Count - 12;
        var size = items.Count - startPos;
        _items.AddRange(items.GetRange(startPos, size));
        */
        .. items.GetRange(0, items.Count > 12 ? 12 : items.Count),
    ];

    /*
        var startPos = items.Count < 12 ? 0 : items.Count - 12;
        var size = items.Count - startPos;
        _items.AddRange(items.GetRange(startPos, size));
        */

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_items.Count);
        foreach (var item in _items)
            stream.Write(item);
        return stream;
    }
}
