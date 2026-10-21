using AAEmu.Game.Models.Game.Animation;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Chooses the fire animation a skill plays from the pose-specific columns on the skill row.
/// </summary>
/// <remarks>
/// Shipped skills carry up to four pose overrides next to <c>fire_anim_id</c>:
/// <c>twohand_fire_anim_id</c>, <c>dual_wield_fire_anim_id</c>, the three instrument columns, and
/// <c>shot_gun_fire_anim_id</c>. The default column is usually the one-hand (or <c>all_</c>) clip; a
/// caster with a two-hand <em>sword/blunt/spear</em> loadout (<c>enum_anim_poses.twohand</c>) must take
/// the matching override when it is authored, because that is also the row whose combat-sync time
/// <c>use_anim_time</c> delays the hit by.
/// A staff is also <see cref="WeaponWieldKind.TwoHanded"/> on the slot, but its holdable pose is
/// <c>staff</c> — there is no staff fire-anim column, so it must keep the default clip (never the
/// two-hand sword override). A zero override means "no pose row" and falls back to the default —
/// never invent a clip.
/// </remarks>
public static class SkillFireAnimRules
{
    /// <summary>
    /// Resolves the fire-anim id for the caster's current loadout. Instrument and shotgun overrides
    /// win when the matching holdable is equipped and the skill authored that column; otherwise the
    /// wield kind + holdable pose pick between the two-hand, dual-wield, and default columns.
    /// </summary>
    /// <remarks>
    /// Instruments live in <c>EquipmentItemSlot.Musical</c>, not mainhand — pass that slot's
    /// holdable id as <paramref name="musicalHoldableId"/>. Shotguns are the ranged slot.
    /// </remarks>
    public static uint ResolveFireAnimId(
        uint defaultFireAnimId,
        uint twoHandFireAnimId,
        uint dualWieldFireAnimId,
        uint shotGunFireAnimId,
        uint stringInstrumentFireAnimId,
        uint percussionInstrumentFireAnimId,
        uint tubeInstrumentFireAnimId,
        WeaponWieldKind wieldKind,
        uint mainhandPoseId,
        uint rangedHoldableId,
        uint musicalHoldableId,
        uint stringInstrumentHoldableId,
        uint percussionInstrumentHoldableId,
        uint tubeInstrumentHoldableId,
        uint shotGunHoldableId)
    {
        if (musicalHoldableId != 0)
        {
            if (stringInstrumentHoldableId != 0 &&
                musicalHoldableId == stringInstrumentHoldableId &&
                stringInstrumentFireAnimId != 0)
                return stringInstrumentFireAnimId;

            if (percussionInstrumentHoldableId != 0 &&
                musicalHoldableId == percussionInstrumentHoldableId &&
                percussionInstrumentFireAnimId != 0)
                return percussionInstrumentFireAnimId;

            if (tubeInstrumentHoldableId != 0 &&
                musicalHoldableId == tubeInstrumentHoldableId &&
                tubeInstrumentFireAnimId != 0)
                return tubeInstrumentFireAnimId;
        }

        if (shotGunHoldableId != 0 &&
            shotGunFireAnimId != 0 &&
            rangedHoldableId == shotGunHoldableId)
            return shotGunFireAnimId;

        // twohand_fire_anim_id is authored for enum_anim_poses.twohand clips only. Staff (and any
        // other two-slot wield that is not that pose) must not inherit those sword/blunt rows.
        if (wieldKind == WeaponWieldKind.TwoHanded &&
            twoHandFireAnimId != 0 &&
            mainhandPoseId == (uint)AnimPose.TwoHand)
            return twoHandFireAnimId;

        if (wieldKind == WeaponWieldKind.DuelWielded && dualWieldFireAnimId != 0)
            return dualWieldFireAnimId;

        return defaultFireAnimId;
    }

    /// <summary>
    /// Whether <c>use_anim_time</c> should add a server hit delay for this cast.
    /// Auto-attacks (skills 2/3/4) are paced by <c>holdables.speed</c>; their combat-sync is a
    /// client-side hit offset on the swing clip, not a second server clock.
    /// </summary>
    public static bool ArmsUseAnimTimeDelay(bool useAnimTime, bool isAutoAttackWeaponAnim) =>
        useAnimTime && !isAutoAttackWeaponAnim;
}
