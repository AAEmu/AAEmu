using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Synchronizes the item identity attached to each of a unit's 34 equipment slots.
/// </summary>
/// <remarks>
/// The packet references an existing unit, so it must follow <see cref="SCUnitStatePacket"/>.
/// </remarks>
public sealed class SCUnitEquipmentIdsPacket(Unit unit) : GamePacket(SCOffsets.SCUnitEquipmentIdsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unit.ObjId);

        for (var slot = 0; slot < EquipmentSerializer.SlotCount; slot++)
        {
            var itemId = unit.Equipment.GetItemBySlot(slot)?.Id ?? 0UL;
            stream.Write(unchecked((long)itemId));
        }

        return stream;
    }
}
