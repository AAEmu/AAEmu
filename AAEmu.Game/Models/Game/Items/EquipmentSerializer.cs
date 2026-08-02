using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Items;

// (called with the unit's idType) and the character-list lobby record (called with mode 0 = Character).
// Wire: validFlags u64 (bit i set iff slot i is occupied, over 34 slots) + each occupied slot in order
// (empty slots emit nothing) + — for a Character — a trailing per-slot flags u64. Per-slot form depends on
// the unit type and slot range: body-image slots 19-25 (non-Slave) write templateId only; an Npc writes a
// compact {templateId, id, grade} for normal slots and a full item for 27/31-33; everything else writes a full item.
public static class EquipmentSerializer
{
    internal const int SlotCount = 34; // 10.0.2.13 equip-slot count (AAEmu fills 0..27; 28..33 stay empty)

    public static void Write(PacketStream stream, Unit unit, BaseUnitType baseUnitType)
    {
        ulong validFlags = 0;
        for (var i = 0; i < SlotCount; i++)
        {
            if (unit.Equipment.GetItemBySlot(i) != null)
                validFlags |= 1UL << i;
        }
        stream.Write(validFlags);

        if (validFlags == 0 && baseUnitType == BaseUnitType.Npc)
        {
            unit.ModelParams.SetType(UnitCustomModelType.Skin); // NPC with no body and no face
            // Commercial compact NPCs write BodyWeight=1.0; default 0 left ModelParams 4B short of capture.
            if (unit.ModelParams.BodyWeight == 0f)
                unit.ModelParams.BodyWeight = 1f;
        }

        for (var i = 0; i < SlotCount; i++)
        {
            var item = unit.Equipment.GetItemBySlot(i);
            if (item == null)
                continue; // empty slots emit nothing in v10 (validFlags already marked them)

            if (i is >= 19 and <= 25 && baseUnitType != BaseUnitType.Slave)
            {
                stream.Write(item.TemplateId); // body-image slots: templateId only
            }
            else if (baseUnitType == BaseUnitType.Npc)
            {
                if (i == 27 || i is >= 31 and <= 33)
                {
                    stream.Write(item); // full item
                }
                else
                {
                    stream.Write(item.TemplateId); // compact item
                    stream.Write(item.Id);
                    stream.Write(item.Grade);
                }
            }
            else
            {
                stream.Write(item); // full item (Character/Slave/Housing/Mate)
            }
        }

        if (baseUnitType == BaseUnitType.Character)
            stream.Write(0UL); // trailing per-slot flags (reinforce/glow). TODO(v10): real per-slot bits.
    }
}
