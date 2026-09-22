using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Factions;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Hero diplomacy: a seated hero asks a hero of a hostile nation for a peace agreement; an accepted
/// request overlays a neutral relation on the pair for faction_diplomacy_term minutes, then the
/// content relation comes back. Agreements, history and counters live in MySQL so a restart keeps
/// them and expiry still fires on time. The request window, the answer dialog and the history and
/// count queries are the client's faction_relations.lua / tab_relation.lua through
/// CSFactionRelationRequest, CSFactionRelationResponse, CSFactionRelationHistoryGet and
/// CSFactionRelationCountGet.
/// </summary>
public class FactionDiplomacyManager(IFactionManager factionManager, ITaskManager taskManager)
    : Singleton<FactionDiplomacyManager>, ILoadable, IInitializable
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly object _sync = new();
    private IFactionDiplomacyStore _store = new InMemoryFactionDiplomacyStore();
    private readonly Dictionary<(uint, uint), FactionDiplomacyAgreement> _agreements = [];
    private readonly List<FactionDiplomacyAgreement> _history = [];
    private readonly Dictionary<(uint, uint), FactionDiplomacyCount> _counts = [];
    private readonly List<FactionDiplomacyProposal> _proposals = [];
    private FactionDiplomacySweepTask _sweepTask;

    private FactionManager Factions => factionManager as FactionManager ?? FactionManager.Instance;

    public void Load()
    {
        lock (_sync)
        {
            _store = new MySqlFactionDiplomacyStore();
            LoadFromStore(DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Reads the history once content_configs is loaded (faction_diplomacy_history_size is only known
    /// after stage 2), ends agreements that lapsed while the process was down, then arms the next sweep.
    /// </summary>
    public void Initialize()
    {
        lock (_sync)
            LoadHistoryNoLock();
        Sweep(DateTime.UtcNow);
    }

    internal void UseStore(IFactionDiplomacyStore store)
    {
        lock (_sync)
            _store = store ?? new InMemoryFactionDiplomacyStore();
    }

    internal void LoadFromStore(DateTime now)
    {
        _agreements.Clear();
        _history.Clear();
        _counts.Clear();
        _proposals.Clear();

        foreach (var agreement in _store.LoadAgreements())
        {
            var key = FactionDiplomacyRules.NormalizePair(agreement.Faction1, agreement.Faction2);
            _agreements[key] = agreement;
            if (!FactionDiplomacyRules.IsExpired(agreement, now))
                Factions.ApplyDiplomacy(agreement);
        }

        foreach (var count in _store.LoadCounts())
            _counts[(count.CharacterId, count.OtherId)] = count;

        Logger.Info("Loaded {0} faction agreements and {1} counters", _agreements.Count, _counts.Count);
    }

    internal void LoadHistoryNoLock()
    {
        _history.Clear();
        var limit = FactionDiplomacyContentConfig.HistoryLimit;
        if (limit > 0)
            _history.AddRange(_store.LoadHistory(limit));
        Logger.Info("Loaded {0} faction agreement history rows", _history.Count);
    }

    public IReadOnlyList<FactionDiplomacyAgreement> Agreements
    {
        get
        {
            lock (_sync)
                return _agreements.Values.ToList();
        }
    }

    public bool HasPendingProposalFor(uint characterId)
    {
        lock (_sync)
            return _proposals.Any(p => p.TargetId == characterId || p.RequesterId == characterId);
    }

    /// <summary>CSFactionRelationRequest: <paramref name="targetCharacterId"/> is the hero picked in the request window.</summary>
    public bool Request(Character requester, ulong targetCharacterId)
    {
        if (requester == null)
            return false;

        if (!IsConfigured())
        {
            Logger.Debug("Faction diplomacy request from {0} refused: content_configs 45/46/142/288 missing", requester.Name);
            requester.SendErrorMessage(ErrorMessageType.FactionRelationSubjectNotFound);
            return false;
        }

        var now = DateTime.UtcNow;
        var target = targetCharacterId is > 0 and <= uint.MaxValue
            ? WorldManager.Instance.GetCharacterById((uint)targetCharacterId)
            : null;
        var requesterNation = (uint)DominionManager.ResolveOwningFaction(requester);
        var targetNation = target == null ? 0u : (uint)DominionManager.ResolveOwningFaction(target);

        FactionDiplomacyRefusal refusal;
        lock (_sync)
        {
            SweepNoLock(now);
            var context = new FactionDiplomacyRequestContext(
                RequesterIsHero: HeroManager.Instance.IsCurrentHero(requester),
                TargetOnline: target != null,
                TargetIsHero: target != null && HeroManager.Instance.IsCurrentHero(target),
                RequesterNation: requesterNation,
                TargetNation: targetNation,
                RequesterNationIsDiplomacyTarget: IsDiplomacyTarget(requesterNation),
                TargetNationIsDiplomacyTarget: IsDiplomacyTarget(targetNation),
                CurrentState: RelationBetween(requesterNation, targetNation),
                RequesterNationHasAgreement: _agreements.Values.Any(a => a.Involves(requesterNation)),
                TargetNationHasAgreement: _agreements.Values.Any(a => a.Involves(targetNation)),
                RequesterNationHasProposal: _proposals.Any(p => p.RequesterNation == requesterNation || p.TargetNation == requesterNation),
                TargetNationHasProposal: _proposals.Any(p => p.RequesterNation == targetNation || p.TargetNation == targetNation),
                RequestsToday: FactionDiplomacyRules.RequestsUsedToday(CountNoLock(requester.Id, 0), now),
                RequestLimit: FactionDiplomacyContentConfig.RequestLimit,
                DeniesByTarget: (int)Math.Min(int.MaxValue, CountNoLock(target?.Id ?? 0, requester.Id)?.Count ?? 0),
                DenyLimit: FactionDiplomacyContentConfig.DenyLimit);

            refusal = FactionDiplomacyRules.EvaluateRequest(context);
            if (refusal == FactionDiplomacyRefusal.None)
            {
                _proposals.Add(new FactionDiplomacyProposal
                {
                    RequesterId = requester.Id,
                    RequesterName = requester.Name,
                    RequesterNation = requesterNation,
                    TargetId = target!.Id,
                    TargetName = target.Name,
                    TargetNation = targetNation,
                    CreatedAt = now
                });
                ArmSweepNoLock(now);
            }
        }

        if (refusal != FactionDiplomacyRefusal.None)
        {
            Logger.Debug("Faction diplomacy request {0} -> {1} refused: {2}", requester.Name, targetCharacterId, refusal);
            requester.SendErrorMessage(FactionDiplomacyRules.ToError(refusal));
            return false;
        }

        // The answer dialog on the other hero: FACTION_RELATION_REQUESTED(name, factionName).
        target!.SendPacket(new SCFactionRelationRequestPacket(requester.Name, (int)requesterNation));
        Logger.Info("Faction diplomacy: {0} ({1}) asked {2} ({3}) for an agreement", requester.Name, requesterNation, target.Name, targetNation);
        return true;
    }

    /// <summary>CSFactionRelationResponse from the hero the request went to.</summary>
    public bool Respond(Character responder, bool ok)
    {
        if (responder == null)
            return false;

        var now = DateTime.UtcNow;
        FactionDiplomacyProposal proposal;
        FactionDiplomacyRefusal refusal;
        FactionDiplomacyAgreement agreement = null;
        lock (_sync)
        {
            SweepNoLock(now);
            proposal = _proposals.FirstOrDefault(p => p.TargetId == responder.Id);
            refusal = FactionDiplomacyRules.EvaluateResponse(proposal, responder.Id, now, FactionDiplomacyContentConfig.ProposalTimeout);
            if (refusal == FactionDiplomacyRefusal.None)
            {
                _proposals.Remove(proposal);
                if (ok)
                {
                    // The pair may have changed while the dialog was open; re-check the parts that can move.
                    if (RelationBetween(proposal.RequesterNation, proposal.TargetNation) != FactionDiplomacyRules.RequestableState)
                        refusal = FactionDiplomacyRefusal.AlreadyFriendly;
                    else if (_agreements.Values.Any(a => a.Involves(proposal.RequesterNation)))
                        refusal = FactionDiplomacyRefusal.AlreadyHaveOtherRelation;
                    else if (_agreements.Values.Any(a => a.Involves(proposal.TargetNation)))
                        refusal = FactionDiplomacyRefusal.TargetAlreadyHaveOtherRelation;
                    else
                    {
                        agreement = FactionDiplomacyRules.Conclude(proposal, RelationBetween(proposal.RequesterNation, proposal.TargetNation), now, FactionDiplomacyContentConfig.AgreementTerm);
                        ConcludeNoLock(agreement, now);
                    }
                }
                else
                {
                    var deny = FactionDiplomacyRules.BumpDenyCount(CountNoLock(responder.Id, proposal.RequesterId), responder.Id, proposal.RequesterId, now);
                    _counts[(deny.CharacterId, deny.OtherId)] = deny;
                    _store.UpsertCount(deny);
                }

                ArmSweepNoLock(now);
            }
        }

        if (refusal != FactionDiplomacyRefusal.None)
        {
            Logger.Debug("Faction diplomacy answer from {0} refused: {1}", responder.Name, refusal);
            responder.SendErrorMessage(FactionDiplomacyRules.ToError(refusal));
            return false;
        }

        var requester = WorldManager.Instance.GetCharacterById(proposal.RequesterId);
        if (agreement != null)
        {
            PublishAgreement(agreement);
            Logger.Info("Faction diplomacy: {0} accepted {1}; nations {2}/{3} neutral until {4:u}",
                responder.Name, proposal.RequesterName, agreement.Faction1, agreement.Faction2, agreement.ChangeTime);
        }
        else
        {
            Logger.Info("Faction diplomacy: {0} denied {1}", responder.Name, proposal.RequesterName);
        }

        if (requester != null)
        {
            // FACTION_RELATION_ACCEPTED(name, factionName) or FACTION_RELATION_DENIED(name) on the requester.
            requester.SendPacket(new SCFactionRelationResponsePacket(responder.Name, (int)proposal.TargetNation, agreement != null));
            SendCounts(requester);
        }

        return true;
    }

    /// <summary>CSFactionRelationCountGet: the viewer's own daily count and every hero's denial count against them.</summary>
    public void SendCounts(Character character)
    {
        if (character == null)
            return;

        List<FactionDiplomacyCount> counts;
        lock (_sync)
            counts = FactionDiplomacyRules.CountsFor(character.Id, _counts.Values, DateTime.UtcNow);
        character.SendPacket(new SCFactionRelationCountPacket(counts));
    }

    /// <summary>CSFactionRelationHistoryGet: the newest faction_diplomacy_history_size rows, oldest first.</summary>
    public void SendHistory(Character character)
    {
        if (character == null)
            return;

        IReadOnlyList<FactionDiplomacyAgreement> history;
        lock (_sync)
            history = FactionDiplomacyRules.TrimHistory(_history, FactionDiplomacyContentConfig.HistoryLimit);
        character.SendPacket(new SCFactionRelationHistoryPacket(history));
    }

    /// <summary>Ends due agreements and drops unanswered requests. Runs from the sweep task and before each decision.</summary>
    public void Sweep(DateTime now)
    {
        List<FactionDiplomacyAgreement> ended;
        List<FactionDiplomacyProposal> timedOut;
        lock (_sync)
        {
            (ended, timedOut) = SweepNoLock(now);
            ArmSweepNoLock(now);
        }

        foreach (var agreement in ended)
        {
            PublishRelation(agreement.Faction1, agreement.Faction2);
            Logger.Info("Faction diplomacy: agreement {0}/{1} ended", agreement.Faction1, agreement.Faction2);
        }

        foreach (var proposal in timedOut)
        {
            WorldManager.Instance.GetCharacterById(proposal.RequesterId)?.SendErrorMessage(ErrorMessageType.FactionDiplomacyTimeout);
            Logger.Info("Faction diplomacy: request {0} -> {1} timed out", proposal.RequesterName, proposal.TargetName);
        }
    }

    private (List<FactionDiplomacyAgreement> Ended, List<FactionDiplomacyProposal> TimedOut) SweepNoLock(DateTime now)
    {
        var ended = new List<FactionDiplomacyAgreement>();
        foreach (var agreement in _agreements.Values.ToList())
        {
            if (!FactionDiplomacyRules.IsExpired(agreement, now))
                continue;
            _agreements.Remove((agreement.Faction1, agreement.Faction2));
            _store.DeleteAgreement(agreement.Faction1, agreement.Faction2);
            Factions.ClearDiplomacy(agreement.Faction1, agreement.Faction2);
            ended.Add(agreement);
        }

        var timeout = FactionDiplomacyContentConfig.ProposalTimeout;
        var timedOut = _proposals.Where(p => FactionDiplomacyRules.IsProposalTimedOut(p, now, timeout)).ToList();
        foreach (var proposal in timedOut)
            _proposals.Remove(proposal);

        return (ended, timedOut);
    }

    private void ConcludeNoLock(FactionDiplomacyAgreement agreement, DateTime now)
    {
        _agreements[(agreement.Faction1, agreement.Faction2)] = agreement;
        _store.UpsertAgreement(agreement);
        _store.InsertHistory(agreement);
        _history.Add(agreement.Clone());
        var limit = FactionDiplomacyContentConfig.HistoryLimit;
        if (limit > 0 && _history.Count > limit)
            _history.RemoveRange(0, _history.Count - limit);

        var daily = FactionDiplomacyRules.BumpDailyCount(CountNoLock(agreement.UpdaterId, 0), agreement.UpdaterId, now);
        _counts[(daily.CharacterId, daily.OtherId)] = daily;
        _store.UpsertCount(daily);

        Factions.ApplyDiplomacy(agreement);
    }

    private void ArmSweepNoLock(DateTime now)
    {
        if (SingletonContainer.ServiceProvider == null)
            return;

        if (_sweepTask != null)
        {
            taskManager.Cancel(_sweepTask);
            _sweepTask = null;
        }

        var delay = FactionDiplomacyRules.NextSweepDelay(
            now,
            _agreements.Values.Select(a => a.ChangeTime),
            _proposals.Select(p => p.CreatedAt),
            FactionDiplomacyContentConfig.ProposalTimeout);
        if (delay == null)
            return;

        _sweepTask = new FactionDiplomacySweepTask();
        taskManager.Schedule(_sweepTask, delay.Value);
    }

    private FactionDiplomacyCount? CountNoLock(uint characterId, uint otherId) =>
        _counts.TryGetValue((characterId, otherId), out var count) ? count : null;

    private static bool IsConfigured() => FactionDiplomacyRules.IsConfigured(
        FactionDiplomacyContentConfig.AgreementTerm,
        FactionDiplomacyContentConfig.RequestLimit,
        FactionDiplomacyContentConfig.DenyLimit,
        FactionDiplomacyContentConfig.ProposalTimeout);

    /// <summary>system_factions.is_diplomacy_tgt: 't' on 114, 148 and 149 only.</summary>
    private bool IsDiplomacyTarget(uint factionId) =>
        factionId != 0 && factionManager.GetFaction((FactionsEnum)factionId)?.DiplomacyTarget == true;

    private RelationState RelationBetween(uint a, uint b)
    {
        var factionA = factionManager.GetFaction((FactionsEnum)a);
        var factionB = factionManager.GetFaction((FactionsEnum)b);
        return factionA == null || factionB == null ? RelationState.Neutral : factionA.GetRelationState(factionB);
    }

    /// <summary>Everyone online gets the row so nameplates and target checks follow it; loaded zones get the table again if the World relay is on.</summary>
    private void PublishAgreement(FactionDiplomacyAgreement agreement) => PublishRelation(agreement.Faction1, agreement.Faction2);

    private void PublishRelation(uint faction1, uint faction2)
    {
        var relation = Factions.GetRelation((FactionsEnum)faction1, (FactionsEnum)faction2);
        if (relation != null)
            WorldManager.Instance.BroadcastPacketToServer(new SCFactionRelationListPacket([relation]));
        WorldIntegration.RelayFactionRelationsToZones?.Invoke();
    }
}
