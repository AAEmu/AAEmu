using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Models.Game.World.Zones;

public class ConflictZoneEscalationRulesTests
{
    /// <summary>Distinct per-level thresholds so every stage has its own boundary.</summary>
    private static readonly int[] Stepped = [10, 20, 30, 40, 50];

    /// <summary>Shipped zone groups 14/15/16/22/23/26/27 (and 19 for PvP): 50 on every level.</summary>
    private static readonly int[] Fifty = [50, 50, 50, 50, 50];

    /// <summary>Shipped zone groups 17 e_ynystere and 20 w_cross_plains: 400 pvp / 600 npc / 50 quests.</summary>
    private static readonly int[] FourHundred = [400, 400, 400, 400, 400];
    private static readonly int[] SixHundred = [600, 600, 600, 600, 600];
    private static readonly int[] FiftyQuests = [50, 50, 50, 50, 50];

    private static readonly int[] None = [0, 0, 0, 0, 0];

    [Test]
    public async Task IsTroubleState_CoversTensionThroughCrisisOnly()
    {
        // enum_honor_point_war_states 0..4 are trouble_0..trouble_4; 5 battle starts the timed states.
        await Assert.That(ConflictZoneEscalationRules.IsTroubleState(ZoneConflictType.Tension)).IsTrue();
        await Assert.That(ConflictZoneEscalationRules.IsTroubleState(ZoneConflictType.Danger)).IsTrue();
        await Assert.That(ConflictZoneEscalationRules.IsTroubleState(ZoneConflictType.Dispute)).IsTrue();
        await Assert.That(ConflictZoneEscalationRules.IsTroubleState(ZoneConflictType.Unrest)).IsTrue();
        await Assert.That(ConflictZoneEscalationRules.IsTroubleState(ZoneConflictType.Crisis)).IsTrue();
        await Assert.That(ConflictZoneEscalationRules.IsTroubleState(ZoneConflictType.Conflict)).IsFalse();
        await Assert.That(ConflictZoneEscalationRules.IsTroubleState(ZoneConflictType.War)).IsFalse();
        await Assert.That(ConflictZoneEscalationRules.IsTroubleState(ZoneConflictType.Peace)).IsFalse();
    }

    [Test]
    public async Task StateOrder_MatchesTheClientHpwsGlobals()
    {
        // HPWS_TROUBLE_0..4 = 0..4, HPWS_BATTLE = 5, HPWS_WAR = 6, HPWS_PEACE = 7 (x2game-dev.dll 0x3997eee0).
        await Assert.That((byte)ZoneConflictType.Tension).IsEqualTo((byte)0);
        await Assert.That((byte)ZoneConflictType.Crisis).IsEqualTo((byte)4);
        await Assert.That((byte)ZoneConflictType.Conflict).IsEqualTo((byte)5);
        await Assert.That((byte)ZoneConflictType.War).IsEqualTo((byte)6);
        await Assert.That((byte)ZoneConflictType.Peace).IsEqualTo((byte)7);
    }

    [Test]
    public async Task CountsPvpKill_HostileAndNeutralCountFriendlyDoesNot()
    {
        await Assert.That(ConflictZoneEscalationRules.CountsPvpKill(RelationState.Hostile)).IsTrue();
        await Assert.That(ConflictZoneEscalationRules.CountsPvpKill(RelationState.Neutral)).IsTrue();
        await Assert.That(ConflictZoneEscalationRules.CountsPvpKill(RelationState.Friendly)).IsFalse();
    }

    [Test]
    public async Task HasThresholds_NeedsOneNonZeroLevel()
    {
        await Assert.That(ConflictZoneEscalationRules.HasThresholds(None)).IsFalse();
        await Assert.That(ConflictZoneEscalationRules.HasThresholds(null)).IsFalse();
        await Assert.That(ConflictZoneEscalationRules.HasThresholds([])).IsFalse();
        await Assert.That(ConflictZoneEscalationRules.HasThresholds([0, 0, 0, 0, 1])).IsTrue();
        await Assert.That(ConflictZoneEscalationRules.HasThresholds(Fifty)).IsTrue();
    }

    [Test]
    public async Task AcceptsParticipation_OnlyInATroubleStateOfAnUnscheduledZoneWithThresholds()
    {
        await Assert.That(ConflictZoneEscalationRules.AcceptsParticipation(ZoneConflictType.Tension, false, Fifty)).IsTrue();
        await Assert.That(ConflictZoneEscalationRules.AcceptsParticipation(ZoneConflictType.Crisis, false, Fifty)).IsTrue();

        // Conflict, War and Peace are timer states.
        await Assert.That(ConflictZoneEscalationRules.AcceptsParticipation(ZoneConflictType.Conflict, false, Fifty)).IsFalse();
        await Assert.That(ConflictZoneEscalationRules.AcceptsParticipation(ZoneConflictType.War, false, Fifty)).IsFalse();
        await Assert.That(ConflictZoneEscalationRules.AcceptsParticipation(ZoneConflictType.Peace, false, Fifty)).IsFalse();

        // Scheduled zones (54, 56, 140, 147) and zones without thresholds never count.
        await Assert.That(ConflictZoneEscalationRules.AcceptsParticipation(ZoneConflictType.Tension, true, Fifty)).IsFalse();
        await Assert.That(ConflictZoneEscalationRules.AcceptsParticipation(ZoneConflictType.Tension, false, None)).IsFalse();
    }

    [Test]
    public async Task AdvanceFromCounter_StepsOneStageAtEachBoundary()
    {
        // Strictly greater-than: reaching a threshold does nothing, passing it moves one stage.
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 10, Stepped)).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 11, Stepped)).IsEqualTo(ZoneConflictType.Danger);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Danger, 20, Stepped)).IsEqualTo(ZoneConflictType.Danger);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Danger, 21, Stepped)).IsEqualTo(ZoneConflictType.Dispute);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Dispute, 30, Stepped)).IsEqualTo(ZoneConflictType.Dispute);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Dispute, 31, Stepped)).IsEqualTo(ZoneConflictType.Unrest);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Unrest, 40, Stepped)).IsEqualTo(ZoneConflictType.Unrest);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Unrest, 41, Stepped)).IsEqualTo(ZoneConflictType.Crisis);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Crisis, 50, Stepped)).IsEqualTo(ZoneConflictType.Crisis);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Crisis, 51, Stepped)).IsEqualTo(ZoneConflictType.Conflict);
    }

    [Test]
    public async Task AdvanceFromCounter_CountIsCumulativeAcrossStages()
    {
        // The count is not reset between trouble stages, so a single large count clears several.
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 31, Stepped)).IsEqualTo(ZoneConflictType.Unrest);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 51, Stepped)).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 10_000, Stepped)).IsEqualTo(ZoneConflictType.Conflict);
    }

    [Test]
    public async Task AdvanceFromCounter_NeverStepsDown()
    {
        // A declaration or forced advance can put a zone above what its count would give.
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Unrest, 0, Stepped)).IsEqualTo(ZoneConflictType.Unrest);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Crisis, 5, Stepped)).IsEqualTo(ZoneConflictType.Crisis);
    }

    [Test]
    public async Task AdvanceFromCounter_CascadesOnEqualThresholdsLikeTheKillCycle()
    {
        // Shipped zones 14/15/16/22/23/26/27 use 50 for all five levels, so the 51st kill goes
        // straight from Tension to Conflict.
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 50, Fifty)).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 51, Fifty)).IsEqualTo(ZoneConflictType.Conflict);

        // Already in a trouble state: the counter keeps climbing from there.
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Danger, 51, Fifty)).IsEqualTo(ZoneConflictType.Conflict);

        // Conflict/War/Peace are not reached by a counter.
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.War, 51, Fifty)).IsEqualTo(ZoneConflictType.War);
    }

    [Test]
    public async Task AdvanceFromCounter_ShippedYnystereAndCrossPlainsThresholds()
    {
        // conflict_zones rows 17 and 20: num_kills_N 400, num_npc_kills_N 600, num_quest_completions_N 50.
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 400, FourHundred)).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 401, FourHundred)).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 600, SixHundred)).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 601, SixHundred)).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 50, FiftyQuests)).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 51, FiftyQuests)).IsEqualTo(ZoneConflictType.Conflict);
    }

    [Test]
    public async Task AdvanceFromCounter_DoesNothingWhenTheZoneHasNoThresholds()
    {
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 10_000, None)).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(ConflictZoneEscalationRules.AdvanceFromCounter(ZoneConflictType.Tension, 10_000, null)).IsEqualTo(ZoneConflictType.Tension);
    }

    [Test]
    public async Task AdvanceByParticipation_TakesTheHighestCounter()
    {
        // Shipped zone 14: 50 pvp kills / 300 npc kills / 15 quest completions per level.
        int[] pvp = [50, 50, 50, 50, 50];
        int[] npc = [300, 300, 300, 300, 300];
        int[] quest = [15, 15, 15, 15, 15];

        // Below every threshold.
        await Assert.That(ConflictZoneEscalationRules.AdvanceByParticipation(
            ZoneConflictType.Tension, 49, pvp, 299, npc, 14, quest)).IsEqualTo(ZoneConflictType.Tension);

        // Only the quest counter crossed.
        await Assert.That(ConflictZoneEscalationRules.AdvanceByParticipation(
            ZoneConflictType.Tension, 0, pvp, 0, npc, 16, quest)).IsEqualTo(ZoneConflictType.Conflict);

        // Only the npc counter crossed.
        await Assert.That(ConflictZoneEscalationRules.AdvanceByParticipation(
            ZoneConflictType.Tension, 0, pvp, 301, npc, 0, quest)).IsEqualTo(ZoneConflictType.Conflict);

        // Counters do not add up: 49 kills, 299 npc kills and 14 quests together are still Tension.
        await Assert.That(ConflictZoneEscalationRules.AdvanceByParticipation(
            ZoneConflictType.Tension, 49, pvp, 299, npc, 14, quest)).IsEqualTo(ZoneConflictType.Tension);
    }

    [Test]
    public async Task AdvanceByParticipation_StepsOneStageWithSteppedThresholds()
    {
        // Each counter has its own ladder; the furthest one decides the stage.
        await Assert.That(ConflictZoneEscalationRules.AdvanceByParticipation(
            ZoneConflictType.Tension, 11, Stepped, 0, Stepped, 0, Stepped)).IsEqualTo(ZoneConflictType.Danger);
        await Assert.That(ConflictZoneEscalationRules.AdvanceByParticipation(
            ZoneConflictType.Tension, 11, Stepped, 21, Stepped, 0, Stepped)).IsEqualTo(ZoneConflictType.Dispute);
        await Assert.That(ConflictZoneEscalationRules.AdvanceByParticipation(
            ZoneConflictType.Tension, 11, Stepped, 21, Stepped, 41, Stepped)).IsEqualTo(ZoneConflictType.Crisis);
    }

    [Test]
    public async Task NextTimedState_WalksTheChainOneStepAtATime()
    {
        await Assert.That(ConflictZoneEscalationRules.NextTimedState(ZoneConflictType.Tension, true, 70)).IsEqualTo(ZoneConflictType.Danger);
        await Assert.That(ConflictZoneEscalationRules.NextTimedState(ZoneConflictType.Danger, true, 70)).IsEqualTo(ZoneConflictType.Dispute);
        await Assert.That(ConflictZoneEscalationRules.NextTimedState(ZoneConflictType.Dispute, true, 70)).IsEqualTo(ZoneConflictType.Unrest);
        await Assert.That(ConflictZoneEscalationRules.NextTimedState(ZoneConflictType.Unrest, true, 70)).IsEqualTo(ZoneConflictType.Crisis);
        await Assert.That(ConflictZoneEscalationRules.NextTimedState(ZoneConflictType.Crisis, true, 70)).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(ConflictZoneEscalationRules.NextTimedState(ZoneConflictType.Conflict, true, 70)).IsEqualTo(ZoneConflictType.War);
        await Assert.That(ConflictZoneEscalationRules.NextTimedState(ZoneConflictType.War, true, 70)).IsEqualTo(ZoneConflictType.Peace);
    }

    [Test]
    public async Task NextTimedState_AfterPeaceDependsOnParticipationThresholds()
    {
        // Zone 14 (thresholds): a fresh count starts at Tension. Zone 57 (none): straight back to Conflict.
        await Assert.That(ConflictZoneEscalationRules.NextTimedState(ZoneConflictType.Peace, true, 70)).IsEqualTo(ZoneConflictType.Tension);
        await Assert.That(ConflictZoneEscalationRules.NextTimedState(ZoneConflictType.Peace, false, 60)).IsEqualTo(ZoneConflictType.Conflict);
    }

    [Test]
    public async Task NextTimedState_WarSkipsPeaceWhenPeaceMinIsZero()
    {
        // Zone 19 w_the_carcass: conflict_min 720, war_min 720, peace_min 0.
        await Assert.That(ConflictZoneEscalationRules.NextTimedState(ZoneConflictType.War, true, 0)).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(ConflictZoneEscalationRules.NextTimedState(ZoneConflictType.War, false, 0)).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(ConflictZoneEscalationRules.NextTimedState(ZoneConflictType.War, true, 1)).IsEqualTo(ZoneConflictType.Peace);
    }

    [Test]
    public async Task TimedStateMinutes_ReadsTheRowDurations()
    {
        // Zone 14: conflict_min 10, war_min 90, peace_min 70.
        await Assert.That(ConflictZoneEscalationRules.TimedStateMinutes(ZoneConflictType.Conflict, 10, 90, 70)).IsEqualTo(10);
        await Assert.That(ConflictZoneEscalationRules.TimedStateMinutes(ZoneConflictType.War, 10, 90, 70)).IsEqualTo(90);
        await Assert.That(ConflictZoneEscalationRules.TimedStateMinutes(ZoneConflictType.Peace, 10, 90, 70)).IsEqualTo(70);
    }

    [Test]
    public async Task TimedStateMinutes_TroubleStatesHaveNoTimer()
    {
        await Assert.That(ConflictZoneEscalationRules.TimedStateMinutes(ZoneConflictType.Tension, 10, 90, 70)).IsEqualTo(0);
        await Assert.That(ConflictZoneEscalationRules.TimedStateMinutes(ZoneConflictType.Crisis, 10, 90, 70)).IsEqualTo(0);
    }
}
