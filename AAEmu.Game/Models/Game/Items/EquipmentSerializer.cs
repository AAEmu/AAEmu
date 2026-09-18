using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Items;

// (called with the unit's idType) and the character-list lobby record (called with mode 0 = Character).
// Wire: validFlags u64 (bit i set iff slot i is occupied, over 34 slots) + each occupied slot in order
// (empty slots emit nothing) plus, for a Character, a trailing per-slot flags u64. Per-slot form depends on
// the unit type and slot range: body-image slots 19-25 (non-Slave) write templateId only; an Npc writes a
// compact {templateId, id, grade} for normal slots and a full item for 27/31-33. Character, Slave,
// Housing, Mate, and type 7 write full items; Transfer and Shipyard have no normal-slot branch.
public static class EquipmentSerializer
{
    public const int SlotCount = 34; // 10.0.2.13 equip-slot count (AAEmu fills 0..27; 28..33 stay empty)

    // The 10.0.2.13 client's equipment helper is also invoked by its Butler state serializer with
    // this raw mode.
    // Butler is not a BaseUnitType and must not be represented as a synthetic Unit just to emit gear.
    private const int ButlerMode = 7;

    public static void Write(PacketStream stream, Unit unit, BaseUnitType baseUnitType)
    {
        WriteCore(stream, unit.Equipment.GetItemBySlot, (int)baseUnitType);

        // Per-slot equipment flags: bit i says the synthesis effects of the piece in slot i count
        // toward the wearer's attributes. The client unpacks the word into one byte per slot and
        // asks that byte last, after it has already established the piece has a synthesis pool at
        // all - and skips the whole rolled-attribute contribution when it reads zero. Everything
        // else about the slot keeps working, which is why the character sheet totalled the piece's
        // own stats, its rune and its lunagems correctly and left only the synthesis lines out.
        //
        // An occupancy mask is not the same thing and was rightly rejected before: a piece with no
        // rolled attributes has nothing to switch on. Only pieces actually carrying effects are
        // marked here.
        if (baseUnitType == BaseUnitType.Character)
            stream.Write(BuildRndAttrActivationMask(unit));
    }

    /// <summary>
    /// Writes Butler equipment using the mode-7 branch of the 10.0.2.13 client's equipment helper.
    /// That helper has the same 34-slot mask as unit equipment but serializes
    /// body-image slots 19 through 25 as template ids only.
    /// </summary>
    public static void WriteButler(PacketStream stream, IReadOnlyDictionary<int, Item> equipment)
    {
        ArgumentNullException.ThrowIfNull(equipment);

        foreach (var (slot, item) in equipment)
        {
            if (slot is < 0 or >= SlotCount)
                throw new ArgumentOutOfRangeException(nameof(equipment), slot, $"Butler equipment slots are 0 through {SlotCount - 1}.");
            if (item is null || item.TemplateId == 0)
                throw new ArgumentException("Butler equipment entries must have a non-zero template id.", nameof(equipment));
        }

        WriteCore(stream, slot => equipment.GetValueOrDefault(slot), ButlerMode);
    }

    private static void WriteCore(PacketStream stream, Func<int, Item?> getItemBySlot, int mode)
    {
        ulong validFlags = 0;
        for (var i = 0; i < SlotCount; i++)
        {
            if (getItemBySlot(i) != null)
                validFlags |= 1UL << i;
        }
        stream.Write(validFlags);

        for (var i = 0; i < SlotCount; i++)
        {
            var item = getItemBySlot(i);
            if (item == null)
                continue; // empty slots emit nothing in v10 (validFlags already marked them)

            if (i is >= 19 and <= 25 && mode != (int)BaseUnitType.Slave)
            {
                stream.Write(item.TemplateId); // body-image slots: templateId only
            }
            else if (mode == (int)BaseUnitType.Npc)
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
            else if (mode is (int)BaseUnitType.Character or (int)BaseUnitType.Slave or
                     (int)BaseUnitType.Housing or (int)BaseUnitType.Mate or ButlerMode)
            {
                stream.Write(item);
            }
        }
    }

    /// <summary>
    /// One bit per equipment slot, set where the piece in it carries synthesis effects that should
    /// count toward the wearer's attributes.
    /// </summary>
    /// <remarks>
    /// Also published on its own packet when a worn piece gains, loses or swaps an effect, since the
    /// unit state is not sent again for a change made in place.
    /// </remarks>
    public static ulong BuildRndAttrActivationMask(Unit unit)
    {
        ulong slotFlags = 0;
        for (var i = 0; i < SlotCount; i++)
        {
            if (unit.Equipment.GetItemBySlot(i) is EquipItem equipped && equipped.UsedRndAttrGroupIds.Any())
                slotFlags |= 1UL << i;
        }

        return slotFlags;
    }
}
