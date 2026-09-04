using AAEmu.Game.Models.Game.Indun.Matching;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

public class IndunMatchReadyRulesTests
{
    private static readonly DateTime T0 = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task IsQueueReady_DirectMinZero_IsImmediate()
    {
        await Assert.That(IndunMatchReadyRules.IsQueueReady(T0, T0, applicantCount: 1, maxPlayers: 5,
            minMatchingTimeMs: 0)).IsTrue();
    }

    [Test]
    public async Task IsQueueReady_Perfect_BeforeMin_StaysFalse()
    {
        var now = T0.AddMilliseconds(179_999);
        await Assert.That(IndunMatchReadyRules.IsQueueReady(T0, now, applicantCount: 1, maxPlayers: 5,
            minMatchingTimeMs: 180_000)).IsFalse();
    }

    [Test]
    public async Task IsQueueReady_Perfect_AtMin_IsTrue()
    {
        var now = T0.AddMilliseconds(180_000);
        await Assert.That(IndunMatchReadyRules.IsQueueReady(T0, now, applicantCount: 1, maxPlayers: 5,
            minMatchingTimeMs: 180_000)).IsTrue();
    }

    [Test]
    public async Task IsQueueReady_FullParty_BeforeMin_IsTrue()
    {
        var now = T0.AddMilliseconds(1);
        await Assert.That(IndunMatchReadyRules.IsQueueReady(T0, now, applicantCount: 5, maxPlayers: 5,
            minMatchingTimeMs: 180_000)).IsTrue();
    }

    [Test]
    public async Task IsQueueExpired_RespectsApplyWaiting()
    {
        await Assert.That(IndunMatchReadyRules.IsQueueExpired(T0, T0.AddMilliseconds(3_600_000),
            applyWaitingTimeMs: 3_600_000)).IsTrue();
        await Assert.That(IndunMatchReadyRules.IsQueueExpired(T0, T0.AddMilliseconds(3_599_999),
            applyWaitingTimeMs: 3_600_000)).IsFalse();
        await Assert.That(IndunMatchReadyRules.IsQueueExpired(T0, T0.AddHours(2), applyWaitingTimeMs: 0))
            .IsFalse();
    }

    [Test]
    public async Task IsInviteExpired_CleanupTerm()
    {
        await Assert.That(IndunMatchReadyRules.IsInviteExpired(T0, T0.AddMilliseconds(300_000),
            cleanupTermMs: 300_000)).IsTrue();
        await Assert.That(IndunMatchReadyRules.IsInviteExpired(T0, T0.AddMilliseconds(299_999),
            cleanupTermMs: 300_000)).IsFalse();
    }

    [Test]
    public async Task Preparing_HoldsPlayersUntilTheCopyIsBuilt()
    {
        await Assert.That(IndunMatchReadyRules.NextAfterPreparing(instanceReady: false,
                MatchingInvitationType.Perfect, T0, T0.AddSeconds(25)))
            .IsEqualTo(IndunPrepareOutcome.KeepWaiting);
    }

    [Test]
    public async Task Preparing_OffersTheDialogOnceTheCopyIsReady()
    {
        await Assert.That(IndunMatchReadyRules.NextAfterPreparing(instanceReady: true,
                MatchingInvitationType.Perfect, T0, T0.AddSeconds(25)))
            .IsEqualTo(IndunPrepareOutcome.Offer);
    }

    [Test]
    public async Task Preparing_DirectInvitationSkipsTheDialog()
    {
        await Assert.That(IndunMatchReadyRules.NextAfterPreparing(instanceReady: true,
                MatchingInvitationType.Direct, T0, T0.AddSeconds(25)))
            .IsEqualTo(IndunPrepareOutcome.Enter);
    }

    [Test]
    public async Task Preparing_GivesUpOnACopyThatNeverAnswers()
    {
        var timeout = T0.AddMilliseconds(IndunMatchReadyRules.PrepareTimeoutMs);
        await Assert.That(IndunMatchReadyRules.IsPrepareExpired(T0, timeout.AddMilliseconds(-1))).IsFalse();
        await Assert.That(IndunMatchReadyRules.IsPrepareExpired(T0, timeout)).IsTrue();
        await Assert.That(IndunMatchReadyRules.NextAfterPreparing(instanceReady: false,
                MatchingInvitationType.Perfect, T0, timeout))
            .IsEqualTo(IndunPrepareOutcome.GiveUp);
    }

    [Test]
    public async Task Preparing_ReadyCopyIsOfferedEvenPastTheTimeout()
    {
        var late = T0.AddMilliseconds(IndunMatchReadyRules.PrepareTimeoutMs + 1);
        await Assert.That(IndunMatchReadyRules.NextAfterPreparing(instanceReady: true,
                MatchingInvitationType.Perfect, T0, late))
            .IsEqualTo(IndunPrepareOutcome.Offer);
    }

    [Test]
    public async Task PrepareTimeout_OutlastsTheZoneHostReadyWait()
    {
        // The host reports its own failure first; this is only the backstop for silence.
        await Assert.That(IndunMatchReadyRules.PrepareTimeoutMs).IsGreaterThan(120_000u);
    }

    [Test]
    public async Task AllActiveAccepted_IgnoresDeclined()
    {
        var members = new List<IndunMatchApplicant>
        {
            new(1, 0, T0) { Accepted = true },
            new(2, 0, T0) { Declined = true },
            new(3, 0, T0) { Accepted = true }
        };
        await Assert.That(IndunMatchReadyRules.AllActiveAccepted(members)).IsTrue();
        await Assert.That(IndunMatchReadyRules.AcceptedCount(members)).IsEqualTo(2);
    }

    [Test]
    public async Task AllActiveAccepted_FalseWhenPending()
    {
        var members = new List<IndunMatchApplicant>
        {
            new(1, 0, T0) { Accepted = true },
            new(2, 0, T0) { Accepted = false }
        };
        await Assert.That(IndunMatchReadyRules.AllActiveAccepted(members)).IsFalse();
    }

    [Test]
    public async Task CanPublishPrepared_OnlyWhileSessionStillPreparing()
    {
        await Assert.That(IndunMatchReadyRules.CanPublishPrepared(IndunMatchPhase.Preparing, true))
            .IsTrue();
        await Assert.That(IndunMatchReadyRules.CanPublishPrepared(IndunMatchPhase.Preparing, false))
            .IsFalse();
        await Assert.That(IndunMatchReadyRules.CanPublishPrepared(IndunMatchPhase.Done, true))
            .IsFalse();
        await Assert.That(IndunMatchReadyRules.CanPublishPrepared(IndunMatchPhase.Inviting, true))
            .IsFalse();
    }
}
