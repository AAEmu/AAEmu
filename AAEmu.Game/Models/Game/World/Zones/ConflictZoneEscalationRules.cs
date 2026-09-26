using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// Kill-driven escalation and the timer chain of a conflict zone: which events count, how far a
/// counter moves the zone, and which state follows which.
/// </summary>
/// <remarks>
/// <para>
/// State ids are <c>enum_honor_point_war_states</c>: 0..4 trouble_0..trouble_4 (Tension..Crisis),
/// 5 battle (Conflict), 6 war, 7 peace. The client registers the same eight values as the Lua globals
/// HPWS_TROUBLE_0..4, HPWS_BATTLE, HPWS_WAR and HPWS_PEACE , float constants
/// 0..7) and its HUD shows a trouble state as stage <c>conflictState + 1</c> with the
/// <c>honor_point_war_state_texts</c> title (x2ui/hud/indicators/zone_informer.lua), so the five
/// trouble stages are distinct client states and the escalation stops at battle: War and Peace are
/// timer states.
/// </para>
/// <para>
/// Thresholds are <c>conflict_zones.num_kills_N</c>, <c>num_npc_kills_N</c> and
/// <c>num_quest_completions_N</c>, N being the trouble state the zone is in. Twelve of the 33 rows
/// carry PvP thresholds: zone groups 14/15/16/22/23/26/27 and 19 at 50 on every level, 17/20/102/103
/// at 400. NPC kills: 300 (14/15/16/22/23/26/27), 600 (17/20), 450 (102), 550 (103); zone 19 has none.
/// Quest completions: 15, 50, 30 and 30 for the same groups. Every other row is all zero and only
/// moves by schedule, timer or declaration. No row weights one counter against another, so each
/// event adds one to its own counter and the highest state any counter reaches wins.
/// <c>no_kill_min_N</c> is 0 on every row.
/// </para>
/// <para>
/// The comparison is cumulative and strictly greater-than, as the kill cycle has shipped since the
/// 1.2 server. Whether retail counted each trouble level from zero could not be established: the
/// only binary available here is the shipped zone host, and it holds no <c>num_kills_</c> literal.
/// With equal per-level thresholds (every shipped row) the zone therefore passes the four middle
/// stages in the same kill that leaves Tension.
/// </para>
/// </remarks>
public static class ConflictZoneEscalationRules
{
    /// <summary>Tension..Crisis: the states participation can move. Conflict, War and Peace are timed.</summary>
    public static bool IsTroubleState(ZoneConflictType state) => state < ZoneConflictType.Conflict;

    /// <summary>
    /// Which player kills feed the zone counter: any kill of a non-friendly player, the same set that
    /// earns PvP honor. A friendly-fire kill is a crime (CrimeManager evidence), not war.
    /// </summary>
    public static bool CountsPvpKill(RelationState killerToVictim) => killerToVictim != RelationState.Friendly;

    /// <summary>A counter with all-zero thresholds is not a mechanic of that zone.</summary>
    public static bool HasThresholds(IReadOnlyList<int> thresholds)
    {
        if (thresholds == null)
            return false;

        foreach (var threshold in thresholds)
        {
            if (threshold != 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether one more event on this counter can move the zone: only in a trouble state, only on a
    /// zone without <c>conflict_zone_realtime_schedules</c> rows, and only for a counter that has
    /// thresholds.
    /// </summary>
    public static bool AcceptsParticipation(ZoneConflictType state, bool scheduleDriven, IReadOnlyList<int> thresholds) =>
        !scheduleDriven && IsTroubleState(state) && HasThresholds(thresholds);

    /// <summary>
    /// Highest conflict state reached by one participation counter. Escalation stops at
    /// <see cref="ZoneConflictType.Conflict"/>. A counter whose thresholds are all zero does not
    /// escalate the zone. The comparison is cumulative and strictly greater-than.
    /// </summary>
    public static ZoneConflictType AdvanceFromCounter(ZoneConflictType current, long count, IReadOnlyList<int> thresholds)
    {
        if (!IsTroubleState(current) || !HasThresholds(thresholds))
            return current;

        var level = (int)current;
        var maxLevel = Math.Min(thresholds.Count, (int)ZoneConflictType.Conflict);
        while (level < maxLevel && count > thresholds[level])
            level++;

        return (ZoneConflictType)level;
    }

    /// <summary>
    /// Highest state reached by any of the three participation counters (PvP kills, listed NPC
    /// kills, listed quest completions).
    /// </summary>
    public static ZoneConflictType AdvanceByParticipation(
        ZoneConflictType current,
        long pvpKills, IReadOnlyList<int> pvpThresholds,
        long npcKills, IReadOnlyList<int> npcThresholds,
        long questCompletions, IReadOnlyList<int> questThresholds)
    {
        var best = current;
        best = Max(best, AdvanceFromCounter(current, pvpKills, pvpThresholds));
        best = Max(best, AdvanceFromCounter(current, npcKills, npcThresholds));
        best = Max(best, AdvanceFromCounter(current, questCompletions, questThresholds));
        return best;
    }

    /// <summary>
    /// The state a timer or a forced advance moves the zone to. Trouble states step up one at a time
    /// into Conflict, then War, then Peace. A zone with <c>peace_min</c> 0 (19 w_the_carcass and the
    /// sea groups) has no Peace and returns from War to Conflict. After Peace a zone that has
    /// participation thresholds starts a fresh count at Tension; one without them (schedule, timer or
    /// declaration only) re-enters Conflict.
    /// </summary>
    public static ZoneConflictType NextTimedState(ZoneConflictType current, bool hasParticipationCounters, int peaceMin)
    {
        if (current < ZoneConflictType.Peace)
        {
            return current == ZoneConflictType.War && peaceMin <= 0
                ? ZoneConflictType.Conflict
                : current + 1;
        }

        return hasParticipationCounters ? ZoneConflictType.Tension : ZoneConflictType.Conflict;
    }

    /// <summary>
    /// How long a timed state lasts: <c>conflict_zones.conflict_min</c>, <c>war_min</c> or
    /// <c>peace_min</c>. Trouble states have no timer (0); they end by participation, decay or a
    /// declaration.
    /// </summary>
    public static int TimedStateMinutes(ZoneConflictType state, int conflictMin, int warMin, int peaceMin) =>
        state switch
        {
            ZoneConflictType.Conflict => conflictMin,
            ZoneConflictType.War => warMin,
            ZoneConflictType.Peace => peaceMin,
            _ => 0
        };

    private static ZoneConflictType Max(ZoneConflictType a, ZoneConflictType b) => a >= b ? a : b;
}
