namespace AAEmu.Game.Models.Game.Mate;

/// <summary>
/// Row of <c>mate_equip_slot_packs</c>. The pack an npc points at through
/// <c>mate_equip_slot_pack_id</c> carries the mate's type, which is where <c>enum_mate_types</c>
/// (0 none, 1 ride, 2 battle) is resolved from.
/// </summary>
public class MateEquipSlotPack
{
    public uint Id { get; set; }
    public byte MateTypeId { get; set; }
}
