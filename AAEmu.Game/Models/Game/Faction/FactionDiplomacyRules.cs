using AAEmu.Game.Models;

namespace AAEmu.Game.Models.Game.Faction;

/// <summary>Why a diplomacy request or answer is refused. Each maps to one enum_error_messages id.</summary>
public enum FactionDiplomacyRefusal
{
    None,
    /// <summary>522: a side is not a seated hero, not online, or its nation is not a diplomacy target.</summary>
    SubjectNotFound,
    /// <summary>528: both heroes belong to the same nation.</summary>
    CannotChangeWithSelf,
    /// <summary>524: the two nations are not hostile right now.</summary>
    AlreadyFriendly,
    /// <summary>526: the requester's nation already has a request waiting for an answer.</summary>
    ProposalAlreadyExists,
    /// <summary>1138: the target's nation is still answering another request.</summary>
    Considering,
    /// <summary>1139: the requester's nation already holds an agreement.</summary>
    AlreadyHaveOtherRelation,
    /// <summary>1140: the target's nation already holds an agreement.</summary>
    TargetAlreadyHaveOtherRelation,
    /// <summary>1136: the requester used up today's agreement count.</summary>
    LimitExceeded,
    /// <summary>1137: the target hero already denied this requester the configured number of times.</summary>
    RefuseLimitExceeded,
    /// <summary>525: an answer arrived with nothing pending for that hero.</summary>
    ProposalNotFound,
    /// <summary>1135: the request was not answered inside the dialog timeout.</summary>
    Timeout
}

/// <summary>Everything a request decision needs, gathered by the manager.</summary>
/// <param name="RequesterIsHero">Requester holds a seat in the latest finalized cycle of their nation.</param>
/// <param name="TargetOnline">The requested hero is in the world right now (ui_texts 10481).</param>
/// <param name="TargetIsHero">The requested character holds a seat in their nation.</param>
/// <param name="CurrentState">Relation between the two nations right now.</param>
/// <param name="RequestsToday">Agreements the requester concluded today (UTC day).</param>
/// <param name="DeniesByTarget">Times the target hero denied this requester.</param>
public readonly record struct FactionDiplomacyRequestContext(
    bool RequesterIsHero,
    bool TargetOnline,
    bool TargetIsHero,
    uint RequesterNation,
    uint TargetNation,
    bool RequesterNationIsDiplomacyTarget,
    bool TargetNationIsDiplomacyTarget,
    RelationState CurrentState,
    bool RequesterNationHasAgreement,
    bool TargetNationHasAgreement,
    bool RequesterNationHasProposal,
    bool TargetNationHasProposal,
    int RequestsToday,
    int RequestLimit,
    int DeniesByTarget,
    int DenyLimit);

/// <summary>
/// Hero diplomacy decisions, kept apart from the manager so they run without a world. The flow is
/// the client's faction_relations.lua: a seated hero picks a hostile nation and one of its heroes,
/// X2Nation:RequestDiplomacy(charId, factionId) sends CSFactionRelationRequest, the other hero
/// answers with CSFactionRelationResponse, and an accepted request makes the two nations neutral
/// for faction_diplomacy_term minutes (ui_texts 10473, 10483, 10487).
/// </summary>
public static class FactionDiplomacyRules
{
    /// <summary>ui_texts 10473: an agreed pair "is displayed as a neutral faction" for the term.</summary>
    public const RelationState AgreementState = RelationState.Neutral;

    /// <summary>The request window only offers nations whose relation is UR_HOSTILE (faction_relations.lua FillData).</summary>
    public const RelationState RequestableState = RelationState.Hostile;

    /// <summary>The client sorts the pair ascending before storing it (x2game-dev.dll 0x39398a90).</summary>
    public static (uint Low, uint High) NormalizePair(uint a, uint b) => a <= b ? (a, b) : (b, a);

    /// <summary>Every content value the feature needs is present and positive; otherwise it stays closed.</summary>
    public static bool IsConfigured(TimeSpan term, int requestLimit, int denyLimit, TimeSpan proposalTimeout) =>
        term > TimeSpan.Zero && requestLimit > 0 && denyLimit > 0 && proposalTimeout > TimeSpan.Zero;

    public static FactionDiplomacyRefusal EvaluateRequest(in FactionDiplomacyRequestContext context)
    {
        if (!context.RequesterIsHero)
            return FactionDiplomacyRefusal.SubjectNotFound;
        if (!context.TargetOnline || !context.TargetIsHero)
            return FactionDiplomacyRefusal.SubjectNotFound;
        if (context.RequesterNation == 0 || context.TargetNation == 0)
            return FactionDiplomacyRefusal.SubjectNotFound;
        if (context.RequesterNation == context.TargetNation)
            return FactionDiplomacyRefusal.CannotChangeWithSelf;
        if (!context.RequesterNationIsDiplomacyTarget || !context.TargetNationIsDiplomacyTarget)
            return FactionDiplomacyRefusal.SubjectNotFound;
        if (context.CurrentState != RequestableState)
            return FactionDiplomacyRefusal.AlreadyFriendly;
        // Same order as the client's own pre-check (x2game-dev.dll 0x391c4aa0): own nation first.
        if (context.RequesterNationHasAgreement)
            return FactionDiplomacyRefusal.AlreadyHaveOtherRelation;
        if (context.TargetNationHasAgreement)
            return FactionDiplomacyRefusal.TargetAlreadyHaveOtherRelation;
        if (context.RequesterNationHasProposal)
            return FactionDiplomacyRefusal.ProposalAlreadyExists;
        if (context.TargetNationHasProposal)
            return FactionDiplomacyRefusal.Considering;
        if (context.RequestLimit <= 0 || context.RequestsToday >= context.RequestLimit)
            return FactionDiplomacyRefusal.LimitExceeded;
        if (context.DenyLimit <= 0 || context.DeniesByTarget >= context.DenyLimit)
            return FactionDiplomacyRefusal.RefuseLimitExceeded;
        return FactionDiplomacyRefusal.None;
    }

    /// <summary>An answer is only valid from the hero the request went to, and only inside the timeout.</summary>
    public static FactionDiplomacyRefusal EvaluateResponse(FactionDiplomacyProposal proposal, uint responderId, DateTime now, TimeSpan timeout)
    {
        if (proposal == null || proposal.TargetId != responderId)
            return FactionDiplomacyRefusal.ProposalNotFound;
        if (IsProposalTimedOut(proposal, now, timeout))
            return FactionDiplomacyRefusal.Timeout;
        return FactionDiplomacyRefusal.None;
    }

    public static bool IsProposalTimedOut(FactionDiplomacyProposal proposal, DateTime now, TimeSpan timeout) =>
        proposal != null && timeout > TimeSpan.Zero && now >= proposal.CreatedAt + timeout;

    /// <summary>
    /// The agreement an accepted request creates. It starts now and reverts to
    /// <paramref name="revertState"/> (the pair's content relation) at the end of the term.
    /// </summary>
    public static FactionDiplomacyAgreement Conclude(FactionDiplomacyProposal proposal, RelationState revertState, DateTime now, TimeSpan term)
    {
        var (low, high) = NormalizePair(proposal.RequesterNation, proposal.TargetNation);
        return new FactionDiplomacyAgreement
        {
            Faction1 = low,
            Faction2 = high,
            State = AgreementState,
            NextState = revertState,
            UpdateTime = now,
            ChangeTime = now + term,
            UpdaterId = proposal.RequesterId,
            UpdaterName = proposal.RequesterName,
            ConfirmerId = proposal.TargetId,
            ConfirmerName = proposal.TargetName
        };
    }

    public static bool IsExpired(FactionDiplomacyAgreement agreement, DateTime now) =>
        agreement != null && now >= agreement.ChangeTime;

    /// <summary>
    /// Delay until the next agreement end or proposal timeout, or null when nothing is pending. Never
    /// below one second so a due item is swept once rather than spun on.
    /// </summary>
    public static TimeSpan? NextSweepDelay(DateTime now, IEnumerable<DateTime> agreementEnds, IEnumerable<DateTime> proposalStarts, TimeSpan proposalTimeout)
    {
        DateTime? next = null;
        foreach (var end in agreementEnds)
            next = next == null || end < next ? end : next;
        if (proposalTimeout > TimeSpan.Zero)
        {
            foreach (var start in proposalStarts)
            {
                var due = start + proposalTimeout;
                next = next == null || due < next ? due : next;
            }
        }

        if (next == null)
            return null;
        var delay = next.Value - now;
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : delay;
    }

    /// <summary>ui_texts 10480: the daily count resets at midnight. A count from another UTC day reads as zero.</summary>
    public static int RequestsUsedToday(FactionDiplomacyCount? count, DateTime now)
    {
        if (count == null)
            return 0;
        return IsSameUtcDay(count.Value.UpdatedAt, now) ? (int)Math.Min(int.MaxValue, count.Value.Count) : 0;
    }

    public static bool IsSameUtcDay(DateTime a, DateTime b) => ServerCalendar.AsUtc(a).Date == ServerCalendar.AsUtc(b).Date;

    /// <summary>The value the client subtracts from the daily limit: a fresh row on a new day, plus one.</summary>
    public static FactionDiplomacyCount BumpDailyCount(FactionDiplomacyCount? current, uint characterId, DateTime now)
    {
        var used = RequestsUsedToday(current, now);
        return new FactionDiplomacyCount(characterId, 0, (uint)used + 1, now);
    }

    /// <summary>Denials never reset on their own (ui_texts 10697 gives no period); a new row starts at one.</summary>
    public static FactionDiplomacyCount BumpDenyCount(FactionDiplomacyCount? current, uint heroId, uint requesterId, DateTime now) =>
        new(heroId, requesterId, (current?.Count ?? 0) + 1, now);

    /// <summary>
    /// What SCFactionRelationCount carries for one viewer: the own row keyed (viewer, 0) with the
    /// count used today, then every denial row of a hero against the viewer keyed (hero, viewer).
    /// </summary>
    public static List<FactionDiplomacyCount> CountsFor(uint viewerId, IEnumerable<FactionDiplomacyCount> counts, DateTime now)
    {
        FactionDiplomacyCount? own = null;
        var denies = new List<FactionDiplomacyCount>();
        foreach (var count in counts)
        {
            if (count.CharacterId == viewerId && count.OtherId == 0)
                own = count;
            else if (count.OtherId == viewerId && count.CharacterId != 0)
                denies.Add(count);
        }

        var result = new List<FactionDiplomacyCount>(denies.Count + 1)
        {
            new(viewerId, 0, (uint)RequestsUsedToday(own, now), now)
        };
        result.AddRange(denies);
        return result;
    }

    /// <summary>The client keeps the newest <paramref name="limit"/> rows and reads them newest first from the tail, so the tail is sent oldest first.</summary>
    public static IReadOnlyList<FactionDiplomacyAgreement> TrimHistory(IReadOnlyList<FactionDiplomacyAgreement> history, int limit)
    {
        if (history == null || history.Count == 0 || limit <= 0)
            return [];
        if (history.Count <= limit)
            return history;
        return history.Skip(history.Count - limit).ToList();
    }

    public static ErrorMessageType ToError(FactionDiplomacyRefusal refusal) => refusal switch
    {
        FactionDiplomacyRefusal.SubjectNotFound => ErrorMessageType.FactionRelationSubjectNotFound,
        FactionDiplomacyRefusal.CannotChangeWithSelf => ErrorMessageType.FactionRelationCannotChangeWithSelf,
        FactionDiplomacyRefusal.AlreadyFriendly => ErrorMessageType.FactionRelationAlreadyFriendly,
        FactionDiplomacyRefusal.ProposalAlreadyExists => ErrorMessageType.FactionRelationProposalAlreadyExists,
        FactionDiplomacyRefusal.Considering => ErrorMessageType.FactionDiplomacyConsidering,
        FactionDiplomacyRefusal.AlreadyHaveOtherRelation => ErrorMessageType.AlreadyHaveOtherFactionRelation,
        FactionDiplomacyRefusal.TargetAlreadyHaveOtherRelation => ErrorMessageType.TargetAlreadyHaveOtherFactionRelation,
        FactionDiplomacyRefusal.LimitExceeded => ErrorMessageType.FactionDiplomacyLimitExceeded,
        FactionDiplomacyRefusal.RefuseLimitExceeded => ErrorMessageType.FactionDiplomacyRefuseLimitExceeded,
        FactionDiplomacyRefusal.ProposalNotFound => ErrorMessageType.FactionRelationProposalNotFound,
        FactionDiplomacyRefusal.Timeout => ErrorMessageType.FactionDiplomacyTimeout,
        _ => ErrorMessageType.NoErrorMessage
    };
}
