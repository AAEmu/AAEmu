using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.UnitTests.Game.Models.Game.Faction;

/// <summary>
/// Hero diplomacy decisions: who may ask whom, the daily and denial caps, what an accepted request
/// creates, when it ends, and the shapes the count and history packets carry.
/// </summary>
public class FactionDiplomacyRulesTests
{
    private const uint Nuia = 148;
    private const uint Haranya = 149;
    private const uint Outlaw = 114;

    private static readonly DateTime Now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Term = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static FactionDiplomacyRequestContext Valid() => new(
        RequesterIsHero: true,
        TargetOnline: true,
        TargetIsHero: true,
        RequesterNation: Nuia,
        TargetNation: Haranya,
        RequesterNationIsDiplomacyTarget: true,
        TargetNationIsDiplomacyTarget: true,
        CurrentState: RelationState.Hostile,
        RequesterNationHasAgreement: false,
        TargetNationHasAgreement: false,
        RequesterNationHasProposal: false,
        TargetNationHasProposal: false,
        RequestsToday: 0,
        RequestLimit: 3,
        DeniesByTarget: 0,
        DenyLimit: 3);

    private static FactionDiplomacyProposal Proposal(DateTime? createdAt = null) => new()
    {
        RequesterId = 10,
        RequesterName = "Asker",
        RequesterNation = Haranya,
        TargetId = 20,
        TargetName = "Answerer",
        TargetNation = Nuia,
        CreatedAt = createdAt ?? Now
    };

    private static FactionDiplomacyAgreement Agreement(uint f1, uint f2, DateTime change) => new()
    {
        Faction1 = f1,
        Faction2 = f2,
        State = RelationState.Neutral,
        NextState = RelationState.Hostile,
        UpdateTime = change - Term,
        ChangeTime = change,
        UpdaterId = 10,
        UpdaterName = "Asker",
        ConfirmerId = 20,
        ConfirmerName = "Answerer"
    };

    [Test]
    public async Task NormalizePair_PutsTheSmallerIdFirst()
    {
        await Assert.That(FactionDiplomacyRules.NormalizePair(Haranya, Nuia)).IsEqualTo((Nuia, Haranya));
        await Assert.That(FactionDiplomacyRules.NormalizePair(Nuia, Haranya)).IsEqualTo((Nuia, Haranya));
        await Assert.That(FactionDiplomacyRules.NormalizePair(Nuia, Nuia)).IsEqualTo((Nuia, Nuia));
    }

    [Test]
    public async Task IsConfigured_NeedsEveryContentValue()
    {
        await Assert.That(FactionDiplomacyRules.IsConfigured(Term, 3, 3, Timeout)).IsTrue();
        await Assert.That(FactionDiplomacyRules.IsConfigured(TimeSpan.Zero, 3, 3, Timeout)).IsFalse();
        await Assert.That(FactionDiplomacyRules.IsConfigured(Term, 0, 3, Timeout)).IsFalse();
        await Assert.That(FactionDiplomacyRules.IsConfigured(Term, 3, 0, Timeout)).IsFalse();
        await Assert.That(FactionDiplomacyRules.IsConfigured(Term, 3, 3, TimeSpan.Zero)).IsFalse();
    }

    [Test]
    public async Task Request_TwoHostileHeroNationsPass()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid())).IsEqualTo(FactionDiplomacyRefusal.None);
    }

    [Test]
    public async Task Request_NeedsASeatedHeroOnBothSides()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { RequesterIsHero = false }))
            .IsEqualTo(FactionDiplomacyRefusal.SubjectNotFound);
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { TargetIsHero = false }))
            .IsEqualTo(FactionDiplomacyRefusal.SubjectNotFound);
    }

    [Test]
    public async Task Request_NeedsTheTargetOnline()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { TargetOnline = false }))
            .IsEqualTo(FactionDiplomacyRefusal.SubjectNotFound);
    }

    [Test]
    public async Task Request_RefusesAnUnresolvedNation()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { RequesterNation = 0 }))
            .IsEqualTo(FactionDiplomacyRefusal.SubjectNotFound);
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { TargetNation = 0 }))
            .IsEqualTo(FactionDiplomacyRefusal.SubjectNotFound);
    }

    [Test]
    public async Task Request_RefusesTheOwnNation()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { TargetNation = Nuia }))
            .IsEqualTo(FactionDiplomacyRefusal.CannotChangeWithSelf);
    }

    [Test]
    public async Task Request_BothNationsMustBeDiplomacyTargets()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { RequesterNationIsDiplomacyTarget = false }))
            .IsEqualTo(FactionDiplomacyRefusal.SubjectNotFound);
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { TargetNationIsDiplomacyTarget = false }))
            .IsEqualTo(FactionDiplomacyRefusal.SubjectNotFound);
    }

    [Test]
    public async Task Request_OnlyAHostilePairCanAgree()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { CurrentState = RelationState.Neutral }))
            .IsEqualTo(FactionDiplomacyRefusal.AlreadyFriendly);
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { CurrentState = RelationState.Friendly }))
            .IsEqualTo(FactionDiplomacyRefusal.AlreadyFriendly);
    }

    [Test]
    public async Task Request_OneAgreementPerNationOwnSideFirst()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { RequesterNationHasAgreement = true }))
            .IsEqualTo(FactionDiplomacyRefusal.AlreadyHaveOtherRelation);
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { TargetNationHasAgreement = true }))
            .IsEqualTo(FactionDiplomacyRefusal.TargetAlreadyHaveOtherRelation);
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { RequesterNationHasAgreement = true, TargetNationHasAgreement = true }))
            .IsEqualTo(FactionDiplomacyRefusal.AlreadyHaveOtherRelation);
    }

    [Test]
    public async Task Request_OneOpenProposalPerNation()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { RequesterNationHasProposal = true }))
            .IsEqualTo(FactionDiplomacyRefusal.ProposalAlreadyExists);
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { TargetNationHasProposal = true }))
            .IsEqualTo(FactionDiplomacyRefusal.Considering);
    }

    [Test]
    public async Task Request_StopsAtTheDailyCount()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { RequestsToday = 2 }))
            .IsEqualTo(FactionDiplomacyRefusal.None);
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { RequestsToday = 3 }))
            .IsEqualTo(FactionDiplomacyRefusal.LimitExceeded);
    }

    [Test]
    public async Task Request_StopsAtTheDenialCount()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { DeniesByTarget = 2 }))
            .IsEqualTo(FactionDiplomacyRefusal.None);
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { DeniesByTarget = 3 }))
            .IsEqualTo(FactionDiplomacyRefusal.RefuseLimitExceeded);
    }

    [Test]
    public async Task Request_AMissingLimitClosesTheFeature()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { RequestLimit = 0 }))
            .IsEqualTo(FactionDiplomacyRefusal.LimitExceeded);
        await Assert.That(FactionDiplomacyRules.EvaluateRequest(Valid() with { DenyLimit = 0 }))
            .IsEqualTo(FactionDiplomacyRefusal.RefuseLimitExceeded);
    }

    [Test]
    public async Task Response_OnlyTheAskedHeroMayAnswer()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateResponse(null, 20, Now, Timeout))
            .IsEqualTo(FactionDiplomacyRefusal.ProposalNotFound);
        await Assert.That(FactionDiplomacyRules.EvaluateResponse(Proposal(), 10, Now, Timeout))
            .IsEqualTo(FactionDiplomacyRefusal.ProposalNotFound);
        await Assert.That(FactionDiplomacyRules.EvaluateResponse(Proposal(), 20, Now.AddSeconds(30), Timeout))
            .IsEqualTo(FactionDiplomacyRefusal.None);
    }

    [Test]
    public async Task Response_AfterTheDialogTimeoutIsTooLate()
    {
        await Assert.That(FactionDiplomacyRules.EvaluateResponse(Proposal(), 20, Now.AddSeconds(60), Timeout))
            .IsEqualTo(FactionDiplomacyRefusal.Timeout);
        await Assert.That(FactionDiplomacyRules.IsProposalTimedOut(Proposal(), Now.AddSeconds(59), Timeout)).IsFalse();
        await Assert.That(FactionDiplomacyRules.IsProposalTimedOut(Proposal(), Now.AddSeconds(60), Timeout)).IsTrue();
    }

    [Test]
    public async Task Response_NoTimeoutMeansNoExpiry()
    {
        await Assert.That(FactionDiplomacyRules.IsProposalTimedOut(Proposal(), Now.AddDays(1), TimeSpan.Zero)).IsFalse();
    }

    [Test]
    public async Task Conclude_MakesTheSortedPairNeutralForTheTerm()
    {
        var agreement = FactionDiplomacyRules.Conclude(Proposal(), RelationState.Hostile, Now, Term);

        await Assert.That(agreement.Faction1).IsEqualTo(Nuia);
        await Assert.That(agreement.Faction2).IsEqualTo(Haranya);
        await Assert.That(agreement.State).IsEqualTo(RelationState.Neutral);
        await Assert.That(agreement.NextState).IsEqualTo(RelationState.Hostile);
        await Assert.That(agreement.UpdateTime).IsEqualTo(Now);
        await Assert.That(agreement.ChangeTime).IsEqualTo(Now.AddMinutes(60));
    }

    [Test]
    public async Task Conclude_NamesTheAskerAsUpdaterAndTheAnswererAsConfirmer()
    {
        var agreement = FactionDiplomacyRules.Conclude(Proposal(), RelationState.Hostile, Now, Term);

        await Assert.That(agreement.UpdaterId).IsEqualTo(10u);
        await Assert.That(agreement.UpdaterName).IsEqualTo("Asker");
        await Assert.That(agreement.ConfirmerId).IsEqualTo(20u);
        await Assert.That(agreement.ConfirmerName).IsEqualTo("Answerer");
    }

    [Test]
    public async Task IsExpired_AtTheChangeTimeNotBefore()
    {
        var agreement = Agreement(Nuia, Haranya, Now);

        await Assert.That(FactionDiplomacyRules.IsExpired(agreement, Now.AddSeconds(-1))).IsFalse();
        await Assert.That(FactionDiplomacyRules.IsExpired(agreement, Now)).IsTrue();
        await Assert.That(FactionDiplomacyRules.IsExpired(null, Now)).IsFalse();
    }

    [Test]
    public async Task NextSweepDelay_IsNullWithNothingPending()
    {
        await Assert.That(FactionDiplomacyRules.NextSweepDelay(Now, [], [], Timeout)).IsNull();
    }

    [Test]
    public async Task NextSweepDelay_TakesTheEarliestAgreementEndOrProposalTimeout()
    {
        var delay = FactionDiplomacyRules.NextSweepDelay(Now, [Now.AddMinutes(30), Now.AddMinutes(5)], [Now.AddSeconds(-10)], Timeout);

        await Assert.That(delay).IsEqualTo(TimeSpan.FromSeconds(50));
    }

    [Test]
    public async Task NextSweepDelay_NeverGoesBelowOneSecond()
    {
        await Assert.That(FactionDiplomacyRules.NextSweepDelay(Now, [Now.AddMinutes(-5)], [], Timeout)).IsEqualTo(TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task NextSweepDelay_IgnoresProposalsWithoutATimeout()
    {
        await Assert.That(FactionDiplomacyRules.NextSweepDelay(Now, [], [Now], TimeSpan.Zero)).IsNull();
    }

    [Test]
    public async Task RequestsUsedToday_ResetsOnANewUtcDay()
    {
        var yesterday = new FactionDiplomacyCount(10, 0, 3, Now.AddDays(-1));
        var today = new FactionDiplomacyCount(10, 0, 2, Now.AddHours(-3));
        var unspecifiedKind = new FactionDiplomacyCount(10, 0, 2, DateTime.SpecifyKind(Now.AddHours(-3), DateTimeKind.Unspecified));

        await Assert.That(FactionDiplomacyRules.RequestsUsedToday(null, Now)).IsEqualTo(0);
        await Assert.That(FactionDiplomacyRules.RequestsUsedToday(yesterday, Now)).IsEqualTo(0);
        await Assert.That(FactionDiplomacyRules.RequestsUsedToday(today, Now)).IsEqualTo(2);
        await Assert.That(FactionDiplomacyRules.RequestsUsedToday(unspecifiedKind, Now)).IsEqualTo(2);
    }

    [Test]
    public async Task BumpDailyCount_ContinuesTodayAndRestartsTomorrow()
    {
        var first = FactionDiplomacyRules.BumpDailyCount(null, 10, Now);
        var second = FactionDiplomacyRules.BumpDailyCount(first, 10, Now.AddMinutes(1));
        var nextDay = FactionDiplomacyRules.BumpDailyCount(second, 10, Now.AddDays(1));

        await Assert.That(first).IsEqualTo(new FactionDiplomacyCount(10, 0, 1, Now));
        await Assert.That(second.Count).IsEqualTo(2u);
        await Assert.That(nextDay.Count).IsEqualTo(1u);
    }

    [Test]
    public async Task BumpDenyCount_IsKeyedHeroThenRequesterAndNeverResets()
    {
        var first = FactionDiplomacyRules.BumpDenyCount(null, 20, 10, Now);
        var later = FactionDiplomacyRules.BumpDenyCount(first, 20, 10, Now.AddDays(40));

        await Assert.That(first).IsEqualTo(new FactionDiplomacyCount(20, 10, 1, Now));
        await Assert.That(later.Count).IsEqualTo(2u);
    }

    [Test]
    public async Task CountsFor_LeadsWithTheOwnRowThenTheDenialsAgainstTheViewer()
    {
        var counts = new[]
        {
            new FactionDiplomacyCount(10, 0, 2, Now),
            new FactionDiplomacyCount(20, 10, 3, Now),
            new FactionDiplomacyCount(21, 10, 1, Now),
            new FactionDiplomacyCount(20, 11, 3, Now),
            new FactionDiplomacyCount(11, 0, 1, Now)
        };

        var result = FactionDiplomacyRules.CountsFor(10, counts, Now);

        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result[0]).IsEqualTo(new FactionDiplomacyCount(10, 0, 2, Now));
        await Assert.That(result[1]).IsEqualTo(new FactionDiplomacyCount(20, 10, 3, Now));
        await Assert.That(result[2]).IsEqualTo(new FactionDiplomacyCount(21, 10, 1, Now));
    }

    [Test]
    public async Task CountsFor_AStaleOwnRowReadsAsZero()
    {
        var result = FactionDiplomacyRules.CountsFor(10, [new FactionDiplomacyCount(10, 0, 3, Now.AddDays(-2))], Now);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Count).IsEqualTo(0u);
    }

    [Test]
    public async Task TrimHistory_KeepsTheNewestRowsInOrder()
    {
        var rows = new List<FactionDiplomacyAgreement>
        {
            Agreement(Nuia, Haranya, Now.AddHours(-3)),
            Agreement(Outlaw, Nuia, Now.AddHours(-2)),
            Agreement(Outlaw, Haranya, Now.AddHours(-1))
        };

        var trimmed = FactionDiplomacyRules.TrimHistory(rows, 2);

        await Assert.That(trimmed.Count).IsEqualTo(2);
        await Assert.That(trimmed[0].Faction1).IsEqualTo(Outlaw);
        await Assert.That(trimmed[0].Faction2).IsEqualTo(Nuia);
        await Assert.That(trimmed[1].Faction2).IsEqualTo(Haranya);
        await Assert.That(FactionDiplomacyRules.TrimHistory(rows, 5).Count).IsEqualTo(3);
        await Assert.That(FactionDiplomacyRules.TrimHistory(rows, 0).Count).IsEqualTo(0);
        await Assert.That(FactionDiplomacyRules.TrimHistory(null, 5).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ToError_MapsEachRefusalToItsClientMessage()
    {
        await Assert.That(FactionDiplomacyRules.ToError(FactionDiplomacyRefusal.SubjectNotFound)).IsEqualTo(ErrorMessageType.FactionRelationSubjectNotFound);
        await Assert.That(FactionDiplomacyRules.ToError(FactionDiplomacyRefusal.CannotChangeWithSelf)).IsEqualTo(ErrorMessageType.FactionRelationCannotChangeWithSelf);
        await Assert.That(FactionDiplomacyRules.ToError(FactionDiplomacyRefusal.AlreadyFriendly)).IsEqualTo(ErrorMessageType.FactionRelationAlreadyFriendly);
        await Assert.That(FactionDiplomacyRules.ToError(FactionDiplomacyRefusal.ProposalAlreadyExists)).IsEqualTo(ErrorMessageType.FactionRelationProposalAlreadyExists);
        await Assert.That(FactionDiplomacyRules.ToError(FactionDiplomacyRefusal.Considering)).IsEqualTo(ErrorMessageType.FactionDiplomacyConsidering);
        await Assert.That(FactionDiplomacyRules.ToError(FactionDiplomacyRefusal.AlreadyHaveOtherRelation)).IsEqualTo(ErrorMessageType.AlreadyHaveOtherFactionRelation);
        await Assert.That(FactionDiplomacyRules.ToError(FactionDiplomacyRefusal.TargetAlreadyHaveOtherRelation)).IsEqualTo(ErrorMessageType.TargetAlreadyHaveOtherFactionRelation);
        await Assert.That(FactionDiplomacyRules.ToError(FactionDiplomacyRefusal.LimitExceeded)).IsEqualTo(ErrorMessageType.FactionDiplomacyLimitExceeded);
        await Assert.That(FactionDiplomacyRules.ToError(FactionDiplomacyRefusal.RefuseLimitExceeded)).IsEqualTo(ErrorMessageType.FactionDiplomacyRefuseLimitExceeded);
        await Assert.That(FactionDiplomacyRules.ToError(FactionDiplomacyRefusal.ProposalNotFound)).IsEqualTo(ErrorMessageType.FactionRelationProposalNotFound);
        await Assert.That(FactionDiplomacyRules.ToError(FactionDiplomacyRefusal.Timeout)).IsEqualTo(ErrorMessageType.FactionDiplomacyTimeout);
        await Assert.That(FactionDiplomacyRules.ToError(FactionDiplomacyRefusal.None)).IsEqualTo(ErrorMessageType.NoErrorMessage);
    }

    [Test]
    public async Task ErrorIds_MatchTheClientEnum()
    {
        await Assert.That((short)ErrorMessageType.FactionDiplomacyTimeout).IsEqualTo((short)1135);
        await Assert.That((short)ErrorMessageType.FactionDiplomacyLimitExceeded).IsEqualTo((short)1136);
        await Assert.That((short)ErrorMessageType.FactionDiplomacyRefuseLimitExceeded).IsEqualTo((short)1137);
        await Assert.That((short)ErrorMessageType.FactionDiplomacyConsidering).IsEqualTo((short)1138);
        await Assert.That((short)ErrorMessageType.AlreadyHaveOtherFactionRelation).IsEqualTo((short)1139);
        await Assert.That((short)ErrorMessageType.TargetAlreadyHaveOtherFactionRelation).IsEqualTo((short)1140);
    }
}
