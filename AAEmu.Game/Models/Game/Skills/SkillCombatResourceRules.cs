namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The combat-resource gates on a cast and on a single effect.
/// </summary>
/// <remarks>
/// <c>skills.min_combat_resource</c> / <c>max_combat_resource</c> were loaded and never read, so a skill
/// that needs a pool to be in a band was castable regardless. The 16 shipped rows split in two:
/// <list type="bullet">
/// <item><description>도발의 외침 / 치유의 외침 / 승자의 외침 (and their variants) name resource 3 with the band
/// 0..5000, which is that pool's own ceiling — they ask for nothing beyond the pool existing.</description></item>
/// <item><description>The test and siege rows state a real band: 34088 증오 분노 테스트 2..4, 34276
/// 저승과 공간 1..1, 43711/43712 카마하 쾌속정 대포 발포 1..1 — a cannon can only fire with exactly one shell
/// loaded, which is the shape the gate is for.</description></item>
/// </list>
///
/// The per-effect columns read the same way, with the pool named explicitly:
/// <list type="bullet">
/// <item><description><c>start_combat_resource</c> / <c>end_combat_resource</c> plus
/// <c>target_combat_resource_id</c> bound the <i>target's</i> pool for that one effect. 41 rows carry a
/// band and 39 name a pool (2 or 15); the bands are authored as ascending pairs — 1..5, 2..5, 6..12,
/// 10..10 — which is why they are read as a range and not as two costs.</description></item>
/// <item><description><c>source_buff_stack_count_min/max</c> and its three siblings are stack bands, and
/// the content proves it: skills 49770, 49864, 49943 and 50072 each carry several effect rows on the same
/// tag 5769 with different bands (1..4, 5..15, 10..15), i.e. one effect per stack band of the caster's
/// buffs. The stack count is the summed <c>buff.Stack</c> of the unit's buffs carrying the row's tag.</description></item>
/// </list>
///
/// <c>excute_effect_on_fire</c> (61 rows) has nothing to do here: this server applies a skill's effects
/// from the fire path only, which is what "execute on fire" asks for. That is recorded rather than
/// implemented so a future second application point can consult it.
/// </remarks>
public static class SkillCombatResourceRules
{
    /// <summary>
    /// Whether a value sits inside an authored band. A band of 0..0 is "no gate"; a reversed pair
    /// (min &gt; max) is read in the order the content meant rather than rejecting everything.
    /// </summary>
    public static bool IsInRange(int current, int min, int max)
    {
        if (min == 0 && max == 0)
            return true;

        var low = Math.Min(min, max);
        var high = Math.Max(min, max);
        return current >= low && current <= high;
    }

    /// <summary>The cast gate: the caster's pool named by <c>skills.combat_resource_id</c>.</summary>
    public static bool AllowsCast(int minCombatResource, int maxCombatResource, int current)
        => IsInRange(current, minCombatResource, maxCombatResource);

    /// <summary>
    /// The per-effect gate: the target's pool named by <c>target_combat_resource_id</c> must sit inside
    /// <c>start_combat_resource</c>..<c>end_combat_resource</c>. An effect that names no pool is not gated.
    /// </summary>
    public static bool AllowsEffect(int startCombatResource, int endCombatResource, int targetCombatResourceId,
        int targetCurrent)
        => targetCombatResourceId <= 0 || IsInRange(targetCurrent, startCombatResource, endCombatResource);

    /// <summary>A stack band, with 0..0 meaning "no gate".</summary>
    public static bool AllowsStackBand(int stackCount, int min, int max)
        => IsInRange(stackCount, min, max);

    /// <summary>
    /// The cast-progress chance (<c>start_casting_use_chance</c>..<c>end_casting_use_chance</c>).
    /// </summary>
    /// <remarks>
    /// 48,739 of the 48,744 rows are the default 1..100, and this server applies a skill's effects once,
    /// after the cast has finished, so the end value is the one that applies and <paramref name="roll"/>
    /// is compared against it. The four rows that are not the default (0..39, 1..50, 40..69, 70..99) are
    /// linear bands the client interpolates while the cast runs; collapsing them to their end value is
    /// the closest this server can get without a per-frame effect pipeline.
    /// </remarks>
    public static bool AllowsCastingUseChance(int castingTime, int endCastingUseChance, double roll)
        => castingTime <= 0 || endCastingUseChance >= 100 || roll < endCastingUseChance;
}
