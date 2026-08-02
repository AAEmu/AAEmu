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
        foreach (var (slot, item) in _items)
        {
            stream.Write((sbyte)slot);
            if (item == null)
                stream.Write(0); // EquipView empty type sentinel
            else
                stream.Write(item);
        }

        stream.Write(0UL); // per-entry flags bitmask
        return stream;
    }
}
