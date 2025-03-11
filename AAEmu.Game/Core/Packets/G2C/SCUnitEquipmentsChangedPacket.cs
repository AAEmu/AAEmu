using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitEquipmentsChangedPacket : GamePacket
{
    private readonly uint _objectId;
    private readonly (byte slot, Item item)[] _items;

    public SCUnitEquipmentsChangedPacket(uint objectId, (byte slot, Item item)[] items) : base(SCOffsets.SCUnitEquipmentsChangedPacket, 5)
    {
        _objectId = objectId;
        _items = items;
    }

    public SCUnitEquipmentsChangedPacket(uint objectId, byte slot, Item item) : base(SCOffsets.SCUnitEquipmentsChangedPacket, 5)
    {
        _objectId = objectId;
        _items = [(slot, item)];
    }

    public override PacketStream Write(PacketStream stream)
    {
        var index = 0;
        var ItemFlags = 0;

        stream.WriteBc(_objectId);
        stream.Write((byte)_items.Length); // TODO max 28
        foreach (var (slot, item) in _items)
        {
            stream.Write(slot);
            if (item == null)
                stream.Write(0);
            else
            {
                stream.Write(item);
                //var v15 = (int)item.ItemFlags << index;
                //++index;
                //ItemFlags |= v15;
            }
        }

        stream.Write(ItemFlags); // added for 3.0.3.0

        return stream;
    }
}
