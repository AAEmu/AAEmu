using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class SharedQuestEncounterTests
{
    private static readonly DateTime Start = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static SharedQuestEncounter Create()
    {
        var encounter = new SharedQuestEncounter(1, [2], Start,
            TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(5));
        encounter.TryJoin(1);
        return encounter;
    }

    [Test]
    public async Task TryJoin_SharedEncounter_OnlyAllowsCapturedPartyBeforeRetreat()
    {
        var encounter = Create();
        await Assert.That(encounter.TryJoin(3)).IsFalse();
        await Assert.That(encounter.TryJoin(2)).IsTrue();
        await Assert.That(encounter.TryJoin(1)).IsFalse();
        encounter.BeginDialogue(Start);
        await Assert.That(encounter.TryJoin(2)).IsFalse();
    }

    [Test]
    public async Task Tick_NoDamage_ResetsAfterInactivityAndFinishesAfterDelay()
    {
        var encounter = Create();
        encounter.Tick(Start.AddSeconds(59), true);
        await Assert.That(encounter.CurrentStage).IsEqualTo(SharedQuestEncounter.Stage.Combat);
        encounter.Tick(Start.AddSeconds(60), true);
        await Assert.That(encounter.CurrentStage).IsEqualTo(SharedQuestEncounter.Stage.Resetting);
        await Assert.That(encounter.UnfinishedParticipants.Single()).IsEqualTo(1u);
        encounter.Tick(Start.AddSeconds(64), true);
        await Assert.That(encounter.CurrentStage).IsEqualTo(SharedQuestEncounter.Stage.Resetting);
        encounter.Tick(Start.AddSeconds(65), true);
        await Assert.That(encounter.CurrentStage).IsEqualTo(SharedQuestEncounter.Stage.Finished);
    }

    [Test]
    public async Task RecordDamage_OnlyParticipantDamageExtendsCombatDeadline()
    {
        var encounter = Create();
        encounter.RecordDamage(3, Start.AddSeconds(50));
        await Assert.That(encounter.Deadline).IsEqualTo(Start.AddSeconds(60));
        encounter.RecordDamage(1, Start.AddSeconds(50));
        await Assert.That(encounter.Deadline).IsEqualTo(Start.AddSeconds(110));
        encounter.Tick(Start.AddSeconds(60), true);
        await Assert.That(encounter.CurrentStage).IsEqualTo(SharedQuestEncounter.Stage.Combat);
    }

    [Test]
    public async Task BeginDialogue_Retreat_StartsFixedWindowAndRejectsDamage()
    {
        var encounter = Create();
        await Assert.That(encounter.BeginDialogue(Start.AddSeconds(20))).IsTrue();
        await Assert.That(encounter.CanDamage(1)).IsFalse();
        await Assert.That(encounter.CanTalk(1)).IsTrue();
        await Assert.That(encounter.CanTalk(3)).IsFalse();
        encounter.RecordDamage(1, Start.AddSeconds(60));
        await Assert.That(encounter.Deadline).IsEqualTo(Start.AddSeconds(70));
        await Assert.That(encounter.BeginDialogue(Start.AddSeconds(65))).IsFalse();
        encounter.Tick(Start.AddSeconds(70), true);
        await Assert.That(encounter.CurrentStage).IsEqualTo(SharedQuestEncounter.Stage.Resetting);
        await Assert.That(encounter.UnfinishedParticipants.Single()).IsEqualTo(1u);
    }

    [Test]
    public async Task Tick_OwnerUnavailableBeforeConversation_FailsUnfinishedParticipants()
    {
        var encounter = Create();
        encounter.TryJoin(2);
        encounter.BeginDialogue(Start);
        encounter.Tick(Start.AddSeconds(1), false);
        await Assert.That(encounter.CurrentStage).IsEqualTo(SharedQuestEncounter.Stage.Resetting);
        await Assert.That(encounter.UnfinishedParticipants.Count()).IsEqualTo(2);
    }

    [Test]
    public async Task Tick_ConversationCompleted_PreservesCreditForFinalTurnIn()
    {
        var encounter = Create();
        encounter.BeginDialogue(Start);
        encounter.Complete(1);
        encounter.Tick(Start.AddSeconds(1), false);
        await Assert.That(encounter.CurrentStage).IsEqualTo(SharedQuestEncounter.Stage.Resetting);
        await Assert.That(encounter.UnfinishedParticipants.Count()).IsEqualTo(0);
        encounter.Tick(Start.AddSeconds(6), false);
        await Assert.That(encounter.CurrentStage).IsEqualTo(SharedQuestEncounter.Stage.Finished);
    }

    [Test]
    public async Task Tick_OnePartyMemberMissesDialogue_OnlyThatMemberFails()
    {
        var encounter = Create();
        encounter.TryJoin(2);
        encounter.BeginDialogue(Start);
        encounter.Complete(1);
        encounter.Tick(Start.AddSeconds(10), false);
        await Assert.That(encounter.CurrentStage).IsEqualTo(SharedQuestEncounter.Stage.Dialogue);
        encounter.Tick(Start.AddSeconds(50), false);
        await Assert.That(encounter.UnfinishedParticipants.Single()).IsEqualTo(2u);
    }

    [Test]
    public async Task BeginReset_RepeatedCallbacks_DoNotPostponeRespawn()
    {
        var encounter = Create();
        encounter.BeginReset(Start);
        encounter.BeginReset(Start.AddSeconds(4));
        await Assert.That(encounter.Deadline).IsEqualTo(Start.AddSeconds(5));
        encounter.Tick(Start.AddSeconds(5), false);
        encounter.BeginReset(Start.AddSeconds(6));
        encounter.Tick(Start.AddSeconds(7), false);
        await Assert.That(encounter.CurrentStage).IsEqualTo(SharedQuestEncounter.Stage.Finished);
        await Assert.That(encounter.TryJoin(2)).IsFalse();
    }

    [Test]
    public async Task Tick_CompletedOwnerTurnsInAndLeaves_ResetsWithoutWaitingForDialogueExpiry()
    {
        var encounter = Create();
        encounter.BeginDialogue(Start);
        encounter.Complete(1);
        encounter.RemoveParticipant(1);

        encounter.Tick(Start.AddSeconds(1), false);

        await Assert.That(encounter.CurrentStage).IsEqualTo(SharedQuestEncounter.Stage.Resetting);
        await Assert.That(encounter.UnfinishedParticipants.Count()).IsEqualTo(0);
    }
}
