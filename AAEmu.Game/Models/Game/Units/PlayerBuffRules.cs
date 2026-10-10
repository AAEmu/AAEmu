using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// Buffs that describe a player character's own state — what it wears and wields, and the zone group it
/// stands in. NPCs, mates and slaves share the <see cref="Unit"/> code that applies them (an NPC wears
/// items for its looks), but none of these buffs are theirs.
/// </summary>
public static class PlayerBuffRules
{
    /// <summary>Wield (shield / two-handed / dual-wield), armor-kind and equip-set buffs.</summary>
    public static bool GrantsLoadoutBuffs(BaseUnit unit) => unit is Character;

    /// <summary>The buff a zone group grants to whoever stands in it (<c>zone_groups.buff_id</c>).</summary>
    public static bool ReceivesZoneGroupBuffs(BaseUnit unit) => unit is Character;

    /// <summary>
    /// The zone-group buffs to take off on entering a group that grants <paramref name="newGroupBuffId"/>
    /// (0 = none): every one the unit holds except that one.
    /// </summary>
    public static List<uint> ZoneBuffsToRemove(IEnumerable<uint> heldZoneBuffIds, uint newGroupBuffId) =>
        heldZoneBuffIds.Where(id => id != 0 && id != newGroupBuffId).Distinct().ToList();

    /// <summary>Whether the new group's buff still has to be applied.</summary>
    public static bool ShouldAddZoneBuff(IEnumerable<uint> heldZoneBuffIds, uint newGroupBuffId) =>
        newGroupBuffId != 0 && !heldZoneBuffIds.Contains(newGroupBuffId);
}
