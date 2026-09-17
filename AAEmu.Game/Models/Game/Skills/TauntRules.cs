namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// <c>buffs.taunt</c> (199 rows) and <c>buffs.taunt_with_top_aggro</c> (133 rows): the buff that makes an
/// NPC attack the unit that applied it.
/// </summary>
/// <remarks>
/// The two columns are 1 도발 and 502 강력한 도발, and the descriptions carry the difference. 171 도발,
/// <c>taunt</c> alone, is "도발되어 위협 수치와 상관없이 시전자 공격" — taunted, it attacks the caster
/// regardless of threat, so the target is forced for the buff's duration and threat is untouched. 502
/// 강력한 도발 sets both flags and reads "대상에게 최고 위협 수치" — it also hands the caster the top
/// threat value, which is what keeps the NPC on the taunter after the force is lifted. Every one of the
/// 133 rows carrying <c>taunt_with_top_aggro</c> also carries <c>taunt</c>, so the second flag is only
/// ever an addition to the first.
/// <para>
/// Only the decisions live here. The forced target and the aggro entry are published to the zone, which
/// owns NPC AI and movement, so World requests and never moves the NPC itself.
/// </para>
/// </remarks>
public static class TauntRules
{
    /// <summary>Whether this buff makes its owner attack the unit that applied it.</summary>
    public static bool ForcesTarget(bool taunt, bool tauntWithTopAggro) => taunt || tauntWithTopAggro;

    /// <summary>Whether this buff also hands the caster the top threat value.</summary>
    public static bool GrantsTopAggro(bool taunt, bool tauntWithTopAggro) => taunt && tauntWithTopAggro;

    /// <summary>
    /// The threat value that puts the caster above every entry the NPC already holds.
    /// </summary>
    /// <param name="highestKnown">
    /// The largest total on the NPC's aggro table as World mirrors it, or 0 when World holds no entry.
    /// </param>
    /// <param name="ownKnown">The caster's own total on that table, or 0.</param>
    /// <remarks>
    /// The step is one because "top threat" is a comparison and not an amount: the taunter has to beat the
    /// highest entry, whatever it happens to be, rather than reach a number this code would have to invent.
    /// The zone is free to add its own modifiers on top.
    /// </remarks>
    public static long TopAggroValue(long highestKnown, long ownKnown) =>
        Math.Max(0, Math.Max(highestKnown, ownKnown)) + 1;

    /// <summary>
    /// The derived value as the wire carries it. <c>WZUpdateAggro</c> writes a u32 and
    /// <c>AddUnitAggro</c> takes an int, so the sum of three saturated components is clamped rather than
    /// truncated into a small number.
    /// </summary>
    public static uint PublishableAggro(long value) => (uint)Math.Clamp(value, 0, uint.MaxValue);

    /// <summary>
    /// What to publish when a plain (non top-aggro) taunt ends.
    /// </summary>
    /// <param name="forcedTargetObjId">The unit the taunt forced on the NPC.</param>
    /// <param name="currentTargetObjId">What the NPC is targeting now, as World mirrors it.</param>
    /// <param name="topAggroObjId">
    /// The NPC's current top threat holder, or 0 when World holds no entry for it.
    /// </param>
    /// <returns>
    /// The target to publish, which is 0 — the native clear sentinel — when nothing better is known, or
    /// null when nothing has to be published at all.
    /// </returns>
    /// <remarks>
    /// A taunt that carries <c>taunt_with_top_aggro</c> leaves the caster holding top threat, so the zone's
    /// own pick is already the taunter and there is nothing to release; only the plain taunt has to hand
    /// the NPC back. Nothing is published if the NPC has already moved on by itself either, or a mob that
    /// switched to a healer mid-taunt would be dragged back to whatever World last mirrored.
    /// </remarks>
    public static uint? ReleaseTarget(uint forcedTargetObjId, uint currentTargetObjId, uint topAggroObjId)
    {
        if (forcedTargetObjId == 0 || currentTargetObjId != forcedTargetObjId)
            return null;

        return topAggroObjId == forcedTargetObjId ? null : topAggroObjId;
    }
}
