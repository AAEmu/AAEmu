namespace AAEmu.Game.Models.Game.Mate;

/// <summary>
/// Row of <c>mate_equip_slot_packs</c>. The pack an npc points at through
/// <c>mate_equip_slot_pack_id</c> carries the mate's type, which is where <c>enum_mate_types</c>
/// (0 none, 1 ride, 2 battle) is resolved from, and which of the four equipment positions that kind of
/// mate may wear at all — a pack with all four clear is a mate that wears nothing.
/// </summary>
public class MateEquipSlotPack
{
    public uint Id { get; set; }
    public byte MateTypeId { get; set; }

    public bool Head { get; set; }
    public bool Chest { get; set; }
    public bool Waist { get; set; }
    public bool Feet { get; set; }

    /// <summary>Whether this pack lets the mate wear the given equipment position.</summary>
    public bool AllowsSlot(MateEquipSlot slot)
    {
        return slot switch
        {
            MateEquipSlot.Head => Head,
            MateEquipSlot.Chest => Chest,
            MateEquipSlot.Waist => Waist,
            MateEquipSlot.Feet => Feet,
            _ => false
        };
    }
}

/// <summary>The four positions a mate's pack speaks about, in the order the table names them.</summary>
public enum MateEquipSlot : byte
{
    Head = 1,
    Chest = 2,
    Waist = 3,
    Feet = 4
}
