namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// <c>buffs.aura_radius</c> and its companions: a buff that keeps re-applying another buff
/// (<c>aura_slave_buff_id</c>) to the units around the unit that carries it.
/// </summary>
/// <remarks>
/// The aura buff is the one on the source (a bard's song, a totem's blessing, a boss's miasma) and the
/// slave buff is what the units inside the radius actually receive. 549 rows author a radius and 511 of
/// them name a slave; the two columns are only meaningful together, which is what
/// <see cref="IsAura(int, uint)"/> gates on. Examples from the shipped content: 789 성전 선포 (20 m,
/// raid) grants 1068, 1024 치유의 무곡 (2레벨) (15 m, raid) grants 833 and heals 6~10 a second, 2362
/// 기상의 나팔소리 (9 m, raid) grants 2363, and 2429 태자의 원념 도트댐 오라 (40 m, hostile) grants
/// 2428.
/// <para>
/// The rules here are the decisions only — who is eligible, how far, how many — so they can be tested
/// without a world. The pulse that walks the units and applies or removes the slave buff lives in
/// <c>AuraTask</c>.
/// </para>
/// </remarks>
public static class AuraRules
{
    /// <summary>
    /// How often an aura re-checks who is inside it, in milliseconds, for an aura that authors no
    /// <c>tick</c> of its own. The shipped auras that do carry one use 1000 ms (789 성전 선포, 1024
    /// 치유의 무곡, 2362 기상의 나팔소리), which is the cadence their descriptions are written against
    /// ("생명력을 1초당 6~10씩 회복").
    /// </summary>
    public const int DefaultPulseIntervalMs = 1000;

    /// <summary>The pulse period to schedule: the aura's own <c>tick</c> when it has one.</summary>
    /// <remarks>
    /// The 511 shipped auras author 0 (463 of them), 500, 1000, 2000, 3000, 4000, 5000 or 20000 ms.
    /// </remarks>
    public static int PulseIntervalMs(double tick) =>
        tick > 0 ? (int)Math.Min(tick, int.MaxValue) : DefaultPulseIntervalMs;

    /// <summary>
    /// Whether a template is an aura at all. Both columns are required: the 51 rows with a radius and no
    /// slave have nothing to apply, and the 13 with a slave and no radius have no area to apply it in.
    /// </summary>
    public static bool IsAura(int radius, uint slaveBuffId) => radius > 0 && slaveBuffId > 0;

    /// <summary>Whether a unit at <paramref name="distance"/> is inside the aura.</summary>
    public static bool InRadius(int radius, float distance) => radius > 0 && distance <= radius;

    /// <summary>
    /// <c>aura_max_count</c>: the ceiling on how many units one aura may hold its slave buff on. Zero is
    /// the authored default for 30,572 rows and means no ceiling.
    /// </summary>
    public static bool HasRoom(int maxCount, int alreadyApplied) => maxCount <= 0 || alreadyApplied < maxCount;

    /// <summary>
    /// Whether one candidate may hold the slave buff.
    /// </summary>
    /// <param name="creatorOnly">
    /// <c>aura_creator_only</c> (86 rows): the aura is the caster's own effect and reaches nobody else.
    /// </param>
    /// <param name="childOnly">
    /// <c>aura_child_only</c> (68 rows): the aura reaches the caster and the units it owns. 2405 은신 이동
    /// is the shape — "자신과 탑승자를 은신 상태로 만듭니다" (makes the caster and its rider stealth) at a
    /// radius of 1 m — so the caster counts as its own nearest child.
    /// </param>
    /// <param name="isCreator">Whether the candidate is the unit that applied the aura.</param>
    /// <param name="isOwnedByCreator">
    /// Whether the candidate is a pet, mate, slave or rider of the unit that applied the aura, as
    /// <c>BaseUnit.GetOwnerCharacter()</c> resolves it.
    /// </param>
    /// <param name="relationMatches">
    /// The <c>aura_relation_id</c> decision for this candidate, taken with the same
    /// <c>SkillTargetingUtil.IsRelationValid</c> the area ticks use.
    /// </param>
    public static bool AllowsRecipient(
        bool creatorOnly,
        bool childOnly,
        bool isCreator,
        bool isOwnedByCreator,
        bool relationMatches)
    {
        if (creatorOnly && !isCreator)
            return false;

        if (childOnly && !(isCreator || isOwnedByCreator))
            return false;

        return relationMatches;
    }
}
