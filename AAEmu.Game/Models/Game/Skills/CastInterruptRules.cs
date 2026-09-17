using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Interrupting somebody else's ordinary cast. A skill with a casting time lives in
/// <c>Unit.SkillTask</c> (a <c>CastTask</c> scheduled for <c>skills.casting_time</c>) until it fires or is
/// stopped; the client's own stop goes through <c>CSStopCastingPacket</c>, which cancels the task and lets
/// <c>Skill.Stop</c> close the timeline. DisturbCasting (special type 5) has to do the same to a unit it
/// does not own — the plot branch alone left every normal cast running.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 151 <c>special_effects</c> rows of type 5, 126 of them reachable
/// through <c>effects</c> (14 <c>skill_effects</c>, 108 <c>buff_triggers</c>, 4 <c>buff_tick_effects</c>).
/// <c>value1</c> is the chance in percent: 100 on 123 of the 126 reachable rows, 1 on one (special effect
/// 3799, skill 15537) and 10 on one (997, skill 12390 로우킥). The last one, 20539, is 0 and belongs to
/// skill 31183 샤티곤 군단의 전투함성, whose own text says it "적들의 타게팅과 시전을 해제합니다" (clears the
/// enemies' targeting and casting) — so 0 cannot mean "never"; it is the unset default and behaves like
/// 100. <c>value2</c> is set on 8 reachable rows (100, 1000, 2000, 4000, 5000, 7000) and no shipped row
/// establishes what it counts, so it is not read; postponing a cast instead of cancelling it would be
/// cast-delay work rather than this effect's.
/// </remarks>
public static class CastInterruptRules
{
    /// <summary>
    /// Chance roll. A row with no chance (0) and a row with a full one (100 or more) are both exact
    /// answers — "always take the effect" — not approximations of a lottery; anything between them is a
    /// real percent roll.
    /// </summary>
    public static bool RollSucceeds(int chancePercent, int rollPercent)
    {
        if (chancePercent <= 0 || chancePercent >= 100)
            return true;
        return rollPercent < chancePercent;
    }

    /// <summary>
    /// Stops the cast <paramref name="unit"/> has in flight the way <c>Skill.Use</c> aborts a superseded
    /// one: cancel the scheduled task, mark the skill cancelled so a task already running returns without
    /// casting, then let <c>Skill.Stop</c> broadcast <c>SCCastingStoppedPacket</c> / <c>SCSkillEndedPacket</c>,
    /// clear <c>Unit.SkillTask</c> and release the timeline id.
    /// </summary>
    /// <returns>True when there was an ordinary cast to interrupt.</returns>
    public static bool TryInterrupt(Unit unit)
    {
        var task = unit?.SkillTask;
        if (task?.Skill == null)
            return false;

        task.Cancel();
        task.Skill.Cancelled = true;
        task.Skill.Stop(unit);
        return true;
    }
}
