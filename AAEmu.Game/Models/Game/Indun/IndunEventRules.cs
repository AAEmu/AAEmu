namespace AAEmu.Game.Models.Game.Indun;

/// <summary>
/// Pure rules that keep indun events firing once per cause. The World raises some of its world events
/// more than once for one cause, and every copy of a zone group shares the same event objects, so the
/// per-world sets these rules work on are keyed by world id in the event classes.
/// </summary>
public static class IndunEventRules
{
    /// <summary>
    /// One dungeon NPC death raises <c>WorldEvents.OnUnitKilled</c> twice (Unit.ReduceCurrentHp and
    /// Unit.DoDie, which Npc.DoDie calls through base). The first raise per object id counts.
    /// </summary>
    public static bool ShouldFireKill(ISet<uint> firedObjIds, uint objId) => firedObjIds.Add(objId);

    /// <summary>A combat end counts once per combat start of that object id.</summary>
    public static bool ShouldFireCombatEnd(ISet<uint> inCombatObjIds, uint objId) => inCombatObjIds.Remove(objId);

    /// <summary>
    /// <c>IndunEventNoInAggroList</c> fires on the edge where the last living tagged NPC leaves combat, once
    /// per engagement: armed by the first tagged combat start, disarmed by the fire. A dead tagged NPC is
    /// out of the fight and never counts, so a kill that leaves none of them alive does not fire this.
    /// </summary>
    public static bool ShouldFireNoInAggroList(bool armed, bool anyTaggedNpcAlive, bool anyTaggedNpcInCombat) =>
        armed && anyTaggedNpcAlive && !anyTaggedNpcInCombat;

    /// <summary><c>indun_event_difficult_changeds.min_difficult..max_difficult</c> (row 1: 0..12), inclusive.</summary>
    public static bool DifficultInRange(int difficult, int minDifficult, int maxDifficult) =>
        minDifficult <= difficult && difficult <= maxDifficult;
}
