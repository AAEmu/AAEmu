using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SC_PACKET_UNIT_EQUIPMENTS_CHANGED (0x0BF).
/// uid, num, isCharTransform, then num × { EquipSlot s8, EquipView }, then flags u64.
/// </summary>
public class SCUnitEquipmentsChangedPacket : GamePacket
{
    private readonly uint _objectId;
    private readonly (byte slot, Item item)[] _items;
    private readonly bool _isCharTransform;

    public SCUnitEquipmentsChangedPacket(uint objectId, (byte slot, Item item)[] items, bool isCharTransform = false)
        : base(SCOffsets.SCUnitEquipmentsChangedPacket, 1)
    {
        _objectId = objectId;
        _items = items;
        _isCharTransform = isCharTransform;
    }

    public SCUnitEquipmentsChangedPacket(uint objectId, byte slot, Item item, bool isCharTransform = false)
        : base(SCOffsets.SCUnitEquipmentsChangedPacket, 1)
    {
        _objectId = objectId;
        _items = [(slot, item)];
        _isCharTransform = isCharTransform;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_objectId);
        stream.Write((byte)_items.Length); // client clamps to 34
        stream.Write(_isCharTransform);
        ulong flags = 0;
        for (var index = 0; index < _items.Length; index++)
        {
            var (slot, item) = _items[index];
            stream.Write((sbyte)slot);
            if (item == null)
                stream.Write(0u); // EquipView empty type sentinel (dword == 0)
            else
                stream.Write(item);
            flags |= 1UL << index;
        }

        stream.Write(flags);
        return stream;
    }
}
