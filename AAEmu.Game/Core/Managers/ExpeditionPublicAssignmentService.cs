using System.Collections.Concurrent;
using System.Threading.Channels;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions.PublicAssignments;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.TodayAssignment;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Core.Managers;

public sealed class ExpeditionPublicAssignmentService : ILoadable, IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    // Current-client module initialization proves zero and no advertisement/update path was found.
    // Keep rerolls free until a matching client/server source can advertise a non-zero amount.
    private const ulong SupportedRerollCost = 0;
    private readonly IExpeditionPublicAssignmentRepository _repository;
    private readonly ExpeditionManager _expeditions;
    private readonly IWorldManager _world;
    private readonly IQuestManager _quests;
    private readonly IPublicQuestRewardDeliveryService _rewardDelivery;
    private readonly Channel<QueuedPublicAssignmentProgress> _queue = Channel.CreateUnbounded<QueuedPublicAssignmentProgress>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly ConcurrentDictionary<(uint ExpeditionId, uint RealStep), ExpeditionPublicAssignmentState> _states = [];
    private readonly ConcurrentDictionary<uint, EventSubscription> _subscriptions = [];
    private Task _consumer;
    private long _queued;
    private long _processed;

    public ExpeditionPublicAssignmentService(IExpeditionPublicAssignmentRepository repository,
        ExpeditionManager expeditions, IWorldManager world, IQuestManager quests,
        IPublicQuestRewardDeliveryService rewardDelivery, IGameDataManager gameDataManager)
    {
        _ = gameDataManager; // Ordering-only dependency: content must finish loading before Load().
        _repository = repository;
        _expeditions = expeditions;
        _world = world;
        _quests = quests;
        _rewardDelivery = rewardDelivery;
        ExpeditionPublicAssignmentServices.Set(this);
    }

    public long QueueDepth => Interlocked.Read(ref _queued) - Interlocked.Read(ref _processed);

    public void OnCharacterLogin(Character character)
    {
        var expedition = character?.Expedition;
        if (expedition == null || character.Connection?.ActiveChar != character ||
            _world.GetCharacterById(character.Id) != character)
            return;
        lock (expedition.SyncRoot)
            if (expedition.GetMember(character.Id) == null)
                return;
        // Remove legacy personal sort-6 quests before subscribing to progress events. Shared
        // projection below is the only authority for these contexts.
        foreach (var step in TodayQuestGameData.Instance.AllSteps.Where(candidate => candidate.IsExpeditionPublicBoard))
        foreach (var group in step.Groups)
        foreach (var questId in group.QuestContextIds)
            if (character.Quests.HasQuest(questId))
                character.Quests.DropQuest(questId, true, false);
        var subscription = new EventSubscription(this, character, (uint)expedition.Id);
        _subscriptions.AddOrUpdate(character.Id, subscription, (_, previous) =>
        {
            previous.Dispose();
            return subscription;
        });
        foreach (var step in TodayQuestGameData.Instance.AllSteps.Where(candidate => candidate.IsExpeditionPublicBoard))
            SendState(character, step.RealStep, true);
    }

    public void OnCharacterLogout(Character character)
    {
        if (character == null || !_subscriptions.TryGetValue(character.Id, out var current) ||
            !ReferenceEquals(current.Character, character)) return;
        if (((ICollection<KeyValuePair<uint, EventSubscription>>)_subscriptions)
            .Remove(new KeyValuePair<uint, EventSubscription>(character.Id, current)))
            current.Dispose();
    }

    public void Load()
    {
        foreach (var state in _repository.LoadCurrent(CurrentPeriod))
            _states[(state.ExpeditionId, state.RealStep)] = state;
        foreach (var expedition in _expeditions.Expeditions)
        foreach (var step in TodayQuestGameData.Instance.AllSteps.Where(candidate => candidate.IsExpeditionPublicBoard))
            EnsureState(expedition, step);
        _consumer = Task.Run(ProcessQueue);
    }

    private ExpeditionPublicAssignmentState EnsureState(Expedition expedition, TodayQuestStepTemplate step)
    {
        if (!MeetsExpeditionLevel(step, expedition.Level))
            return null;
        using var persistence = PersistenceOperationScope.Enter();
        var period = CurrentPeriod;
        if (_states.TryGetValue(((uint)expedition.Id, step.RealStep), out var existing) && existing.PeriodStart == period) return existing;
        var options = step.Groups.SelectMany(group => group.QuestContextIds.Select(quest => (group, quest))).ToArray();
        if (options.Length == 0) return null;
        lock (expedition.SyncRoot)
        {
            if (_states.TryGetValue(((uint)expedition.Id, step.RealStep), out existing) && existing.PeriodStart == period) return existing;
            var selected = options[Random.Shared.Next(options.Length)];
            var created = new ExpeditionPublicAssignmentState
            {
                ExpeditionId = (uint)expedition.Id, PeriodStart = period,
                RealStep = step.RealStep, GroupId = selected.group.Id, QuestContextId = selected.quest,
                Status = TodayAssignmentStatus.Progress
            };
            using var connection = _repository.Open(); using var transaction = connection.BeginTransaction();
            if (!_repository.TrySave(connection, transaction, created, 0))
            {
                transaction.Rollback();
                var winner = _repository.LoadCurrent(period).FirstOrDefault(state =>
                    state.ExpeditionId == (uint)expedition.Id && state.RealStep == step.RealStep);
                if (winner != null)
                    _states[((uint)expedition.Id, step.RealStep)] = winner;
                return winner;
            }
            transaction.Commit(); created.Version = 1;
            _states[((uint)expedition.Id, step.RealStep)] = created;
            return created;
        }
    }

    public bool TryEnqueue(Character character, EventArgs source)
    {
        if (character == null || !_subscriptions.TryGetValue(character.Id, out var subscription) ||
            !ReferenceEquals(subscription.Character, character))
            return false;
        return TryEnqueue(subscription, source);
    }

    private bool TryEnqueue(EventSubscription subscription, EventArgs source)
    {
        var character = subscription.Character;
        if (!_subscriptions.TryGetValue(character.Id, out var current) || !ReferenceEquals(current, subscription) ||
            character.Connection?.ActiveChar != character || _world.GetCharacterById(character.Id) != character ||
            character.Expedition == null || (uint)character.Expedition.Id != subscription.ExpeditionId ||
            !PublicQuestProgressEvent.TryCapture(character, source, out var captured))
            return false;
        var wrote = false;
        var period = CurrentPeriod;
        foreach (var state in _states.Values.Where(state => state.ExpeditionId == captured.ExpeditionId &&
                     state.PeriodStart == period && state.Status == TodayAssignmentStatus.Progress))
        {
            if (!_queue.Writer.TryWrite(new QueuedPublicAssignmentProgress(
                    captured, character.Name, state.RealStep, state.PeriodStart,
                    state.SelectionGeneration)))
                return false;
            Interlocked.Increment(ref _queued);
            wrote = true;
        }
        return wrote;
    }

    public void SendState(Character character, uint realStep, bool init)
    {
        if (!TryGetCurrentExpedition(character, out var expedition)) return;
        var step = TodayQuestGameData.Instance.GetStepByRealStep(realStep);
        if (step is not { IsExpeditionPublicBoard: true }) return;
        var state = EnsureState(expedition, step);
        if (state != null)
        {
            character.SendPacket(new SCTodayAssignmentChangedPacket((int)step.Id, (int)state.GroupId,
                (int)state.QuestContextId, (sbyte)state.Status, init));
            SendQuestProjection(character, state, init);
        }
    }

    public bool Reroll(Character owner, uint realStep, ulong clientCost)
    {
        using var persistence = PersistenceOperationScope.Enter();
        if (!TryGetCurrentExpedition(owner, out var expedition) || expedition.OwnerId != owner.Id) return false;
        var step = TodayQuestGameData.Instance.GetStepByRealStep(realStep);
        if (step is not { IsExpeditionPublicBoard: true } || step.Groups.Count == 0) return false;
        var currentState = EnsureState(expedition, step);
        if (currentState == null) return false;
        const ulong configuredCost = SupportedRerollCost;
        if (clientCost != configuredCost) return false;
        ExpeditionPublicAssignmentState next;
        lock (expedition.SyncRoot)
        {
            if (!TryGetCurrentExpedition(owner, out var current) || !ReferenceEquals(current, expedition) ||
                expedition.OwnerId != owner.Id) return false;
            var previous = _states.GetValueOrDefault(((uint)expedition.Id, realStep));
            if (previous?.Status == TodayAssignmentStatus.Done) return false;
            var candidates = step.Groups.SelectMany(group => group.QuestContextIds.Select(quest => (group, quest)))
                .Where(candidate => candidate.quest != previous?.QuestContextId).ToArray();
            if (candidates.Length == 0) return false;
            var selected = candidates[Random.Shared.Next(candidates.Length)];
            next = new ExpeditionPublicAssignmentState
            {
                ExpeditionId = (uint)expedition.Id, PeriodStart = CurrentPeriod,
                RealStep = realStep, GroupId = selected.group.Id, QuestContextId = selected.quest,
                Status = TodayAssignmentStatus.Progress, Version = previous?.Version ?? 0,
                SelectionGeneration = checked((previous?.SelectionGeneration ?? 0) + 1)
            };
            using var connection = _repository.Open(); using var transaction = connection.BeginTransaction();
            if (!_repository.TrySave(connection, transaction, next, next.Version)) { transaction.Rollback(); return false; }
            foreach (var table in new[]
                     {
                         "expedition_public_assignment_contributors", "expedition_public_assignment_claims"
                     })
            {
                using var clear = connection.CreateCommand();
                clear.Transaction = transaction;
                clear.CommandText = $"DELETE FROM {table} WHERE expedition_id=@id AND period_start=@period AND real_step=@step";
                clear.Parameters.AddWithValue("@id", expedition.Id);
                clear.Parameters.AddWithValue("@period", next.PeriodStart);
                clear.Parameters.AddWithValue("@step", next.RealStep);
                clear.ExecuteNonQuery();
            }
            using var debit = connection.CreateCommand(); debit.Transaction = transaction;
            debit.CommandText = "UPDATE expeditions SET last_assignment_update_time=@updated WHERE id=@id";
            debit.Parameters.AddWithValue("@updated", ServerCalendar.UtcNow);
            debit.Parameters.AddWithValue("@id", expedition.Id);
            if (debit.ExecuteNonQuery() != 1) { transaction.Rollback(); return false; }
            transaction.Commit();
            next.Version++;
            expedition.LastAssignmentUpdateTime = ServerCalendar.UtcNow;
            _states[((uint)expedition.Id, realStep)] = next;
        }
        Broadcast(expedition, step, next, false);
        return true;
    }

    private async Task ProcessQueue()
    {
        await foreach (var progress in _queue.Reader.ReadAllAsync())
        {
            try { ApplyProgress(progress); }
            catch (Exception exception) { Logger.Error(exception, "Public assignment progress failed for guild {0}.", progress.Captured.ExpeditionId); }
            finally { Interlocked.Increment(ref _processed); }
        }
    }

    private void ApplyProgress(QueuedPublicAssignmentProgress progress)
    {
        using var persistence = PersistenceOperationScope.Enter();
        var expedition = _expeditions.GetExpedition((FactionsEnum)progress.Captured.ExpeditionId);
        if (expedition == null) return;
        ExpeditionPublicAssignmentState state;
        TodayQuestStepTemplate step;
        PublicQuestStagedRewardDelivery stagedDelivery = null;
        ExpeditionManager.ExpeditionExpCommit stagedExp = null;
        ExpeditionManager.ExpeditionContributionCommit stagedContribution = null;
        DateTime committedAt = default;
        try
        {
        lock (expedition.SyncRoot)
        {
            if (!_states.TryGetValue((progress.Captured.ExpeditionId, progress.RealStep), out state) ||
                !progress.Matches(state) || state.Status != TodayAssignmentStatus.Progress ||
                state.PeriodStart != CurrentPeriod) return;
            step = TodayQuestGameData.Instance.GetStepByRealStep(progress.RealStep);
            if (step is not { IsExpeditionPublicBoard: true } || !MeetsExpeditionLevel(step, expedition.Level))
                return;
            var template = _quests.GetTemplate(state.QuestContextId);
            var objectives = template?.GetFirstComponent(QuestComponentKind.Progress)?.ActTemplates;
            if (objectives == null)
                return;
            PublicQuestObjectiveProgress matched = default;
            var hasMatch = false;
            foreach (var objective in objectives.Where(act => act.CountsAsAnObjective))
            {
                if (!PublicQuestObjectiveMatcher.TryGetProgress(objective, progress.Captured, _quests,
                        out matched))
                    continue;
                if (state.Objectives[matched.ObjectiveIndex] >= matched.Target)
                    continue;
                hasMatch = true;
                break;
            }
            if (!hasMatch)
                return;
            var oldVersion = state.Version;
            var updated = state.Copy();
            var previousObjectiveValue = updated.Objectives[matched.ObjectiveIndex];
            updated.Objectives[matched.ObjectiveIndex] = Math.Min(matched.Target,
                checked(updated.Objectives[matched.ObjectiveIndex] + matched.Delta));
            var appliedDelta = updated.Objectives[matched.ObjectiveIndex] - previousObjectiveValue;
            var objectiveDefinitions = objectives.Where(act => act.CountsAsAnObjective)
                .Select(act => PublicQuestObjectiveMatcher.TryGetDefinition(act, out var index, out var target)
                    ? (Valid: true, Index: index, Target: target)
                    : (Valid: false, Index: byte.MaxValue, Target: 0))
                .ToArray();
            if (objectiveDefinitions.Length > 0 && objectiveDefinitions.All(definition =>
                    definition.Valid && updated.Objectives[definition.Index] >= definition.Target))
            {
                updated.Status = TodayAssignmentStatus.Done;
                updated.CompletedAt = ServerCalendar.UtcNow;
            }
            using var connection = _repository.Open(); using var transaction = connection.BeginTransaction();
            var contributor = expedition.GetMember(progress.Captured.ContributorId);
            // The event was authorized against the live roster before it entered the queue. Preserve
            // that identity so an earned event is not lost merely because the member logs out or leaves
            // before the single consumer reaches it.
            var contributorName = contributor?.Name
                ?? state.Contributors.GetValueOrDefault(progress.Captured.ContributorId)?.CharacterName
                ?? progress.ContributorName;
            if (string.IsNullOrWhiteSpace(contributorName)) return;
            _repository.UpsertContributor(connection, transaction, updated, progress.Captured.ContributorId, contributorName, appliedDelta);
            if (updated.Status == TodayAssignmentStatus.Done)
            {
                if (!PublicQuestRewardBundleResolver.TryResolve(_quests, updated.QuestContextId, out var bundle))
                    throw new InvalidOperationException($"Invalid public assignment reward bundle {updated.QuestContextId}.");
                var contributorIds = updated.Contributors.Keys.Append(progress.Captured.ContributorId).ToHashSet();
                var recipients = expedition.Members.Where(member => contributorIds.Contains(member.CharacterId))
                    .Select(member => new PublicQuestRewardRecipient(member.CharacterId, member.Name)).ToArray();
                if (recipients.Length > 0 &&
                    !_rewardDelivery.TryStageRewards(bundle, recipients, connection, transaction, out stagedDelivery))
                    throw new InvalidOperationException("Unable to stage public assignment rewards.");
                if (!_expeditions.TryStageContributionCredits(expedition,
                        recipients.Select(recipient => recipient.CharacterId).ToArray(),
                        bundle.ContributionPoints, connection, transaction, out stagedContribution))
                    throw new InvalidOperationException("Unable to stage public assignment contribution rewards.");
                foreach (var recipient in recipients)
                {
                    using var claim = connection.CreateCommand(); claim.Transaction = transaction;
                    claim.CommandText = "INSERT INTO expedition_public_assignment_claims(expedition_id,period_start,real_step,character_id,character_name,delivered_at) VALUES(@id,@period,@step,@character,@name,@delivered)";
                    claim.Parameters.AddWithValue("@id", expedition.Id); claim.Parameters.AddWithValue("@period", updated.PeriodStart);
                    claim.Parameters.AddWithValue("@step", updated.RealStep); claim.Parameters.AddWithValue("@character", recipient.CharacterId);
                    claim.Parameters.AddWithValue("@name", recipient.Name); claim.Parameters.AddWithValue("@delivered", ServerCalendar.UtcNow);
                    claim.ExecuteNonQuery();
                }
                var now = ServerCalendar.UtcNow;
                if (bundle.ExpeditionExperience > 0 &&
                    !_expeditions.TryStageExp(expedition, bundle.ExpeditionExperience, connection, transaction,
                        out stagedExp))
                    throw new InvalidOperationException("Unable to stage public assignment guild experience.");
                committedAt = now;
                using var guild = connection.CreateCommand(); guild.Transaction = transaction;
                guild.CommandText = "UPDATE expeditions SET last_assignment_update_time=@now WHERE id=@id";
                guild.Parameters.AddWithValue("@now", now);
                guild.Parameters.AddWithValue("@id", expedition.Id);
                if (guild.ExecuteNonQuery() != 1) throw new InvalidOperationException("Unable to persist public assignment guild reward.");
                updated.GuildRewarded = true;
            }
            if (!_repository.TrySave(connection, transaction, updated, oldVersion))
            {
                transaction.Rollback();
                stagedDelivery?.Dispose();
                return;
            }
            var reconciledAfterCommitException = false;
            try
            {
                transaction.Commit();
            }
            catch (Exception commitException)
            {
                try
                {
                    var durable = _repository.LoadCurrent(updated.PeriodStart).FirstOrDefault(candidate =>
                        candidate.ExpeditionId == updated.ExpeditionId && candidate.RealStep == updated.RealStep);
                    if (durable == null || durable.Version != oldVersion + 1 ||
                        durable.SelectionGeneration != updated.SelectionGeneration ||
                        durable.Status != updated.Status || durable.GuildRewarded != updated.GuildRewarded)
                        throw commitException;
                    updated = durable;
                    reconciledAfterCommitException = true;
                    Logger.Warn(commitException,
                        "Public assignment commit reported failure but durable state confirms guild {0}, step {1}, version {2}.",
                        updated.ExpeditionId, updated.RealStep, updated.Version);
                }
                catch
                {
                    // The outcome remains ambiguous. Protect item IDs which may already belong to
                    // durable mail; never treat this as an ordinary rollback.
                    stagedDelivery?.MarkCommitted();
                    throw;
                }
            }
            stagedDelivery?.MarkCommitted();
            stagedExp?.Apply();
            stagedContribution?.Apply();
            if (!reconciledAfterCommitException)
                updated.Version++;
            if (updated.GuildRewarded)
            {
                expedition.LastAssignmentUpdateTime = committedAt;
            }
            if (!reconciledAfterCommitException)
            {
                var total = updated.Contributors.GetValueOrDefault(progress.Captured.ContributorId)?.Contribution ?? 0;
                updated.Contributors[progress.Captured.ContributorId] = new(progress.Captured.ContributorId,
                    contributorName, total + (ulong)appliedDelta);
            }
            _states[(progress.Captured.ExpeditionId, progress.RealStep)] = updated;
            state = updated;
        }
        }
        catch
        {
            stagedDelivery?.Dispose();
            throw;
        }
        stagedDelivery?.Publish();
        stagedDelivery?.Dispose();
        stagedExp?.Publish();
        stagedContribution?.Publish();
        if (state.Status == TodayAssignmentStatus.Done) expedition.SendDescriptor(_world);
        Broadcast(expedition, step, state, false);
    }

    private bool TryGetCurrentExpedition(Character character, out Expedition expedition)
    {
        expedition = character?.Expedition;
        if (expedition == null || character.Connection?.ActiveChar != character ||
            _world.GetCharacterById(character.Id) != character)
            return false;
        lock (expedition.SyncRoot)
            return expedition.GetMember(character.Id) != null;
    }

    private DateTime CurrentPeriod => GetPeriodStart(ServerCalendar.UtcNow,
        (int)(_expeditions.GetContentConfig("expedition_public_quest_reset_weekly_day", 1) % 7));

    public static DateTime GetPeriodStart(DateTime utcNow, int resetDay)
    {
        var normalizedDay = ((resetDay % 7) + 7) % 7;
        var daysSinceReset = ((int)utcNow.DayOfWeek - normalizedDay + 7) % 7;
        return utcNow.ToUniversalTime().Date.AddDays(-daysSinceReset);
    }

    public void OnCalendarBoundary()
    {
        foreach (var expedition in _expeditions.Expeditions)
        foreach (var step in TodayQuestGameData.Instance.AllSteps.Where(candidate => candidate.IsExpeditionPublicBoard))
        {
            var previous = _states.GetValueOrDefault(((uint)expedition.Id, step.RealStep));
            var current = EnsureState(expedition, step);
            if (current != null && previous?.PeriodStart != current.PeriodStart)
                Broadcast(expedition, step, current, true);
        }
    }

    private static bool MeetsExpeditionLevel(TodayQuestStepTemplate step, uint level) =>
        (step.LevelMin <= 0 || level >= step.LevelMin) &&
        (step.LevelMax <= 0 || level <= step.LevelMax);

    private void Broadcast(Expedition expedition, TodayQuestStepTemplate step,
        ExpeditionPublicAssignmentState state, bool init)
    {
        expedition.SendPacket(new SCTodayAssignmentChangedPacket((int)step.Id, (int)state.GroupId,
            (int)state.QuestContextId, (sbyte)state.Status, init), _world);
        ExpeditionMember[] members;
        lock (expedition.SyncRoot) members = expedition.Members.ToArray();
        foreach (var member in members)
            if (_world.GetCharacterById(member.CharacterId) is { } character)
                SendQuestProjection(character, state, init);
    }

    private void SendQuestProjection(Character character, ExpeditionPublicAssignmentState state, bool init)
    {
        var template = _quests.GetTemplate(state.QuestContextId);
        if (template == null) return;
        var view = new PublicAssignmentQuestWireState(template, state);
        if (state.Status == TodayAssignmentStatus.Done)
            character.SendPacket(new SCQuestContextCompletedPacket(template.Id, view.ComponentId));
        else if (init)
            character.SendPacket(new SCQuestContextStartedPacket(view, view.ComponentId));
        else
            character.SendPacket(new SCQuestContextUpdatedPacket(view, view.ComponentId));
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions.Values) subscription.Dispose();
        _subscriptions.Clear();
        _queue.Writer.TryComplete();
        try { _consumer?.Wait(TimeSpan.FromSeconds(30)); } catch (Exception exception) { Logger.Error(exception); }
        ExpeditionPublicAssignmentServices.Clear(this);
    }

    private sealed class EventSubscription : IDisposable
    {
        private readonly ExpeditionPublicAssignmentService _owner;
        public Character Character { get; }
        public uint ExpeditionId { get; }
        public EventSubscription(ExpeditionPublicAssignmentService owner, Character character, uint expeditionId)
        {
            _owner = owner; Character = character; ExpeditionId = expeditionId;
            character.Events.OnQuestProgressStat += Forward;
            character.Events.OnLaborPower += Forward;
            character.Events.OnZoneKill += Forward;
            character.Events.OnMonsterGroupHunt += Forward;
            character.Events.OnItemUse += Forward;
            character.Events.OnQuestComplete += Forward;
        }
        private void Forward(object sender, EventArgs args) => _owner.TryEnqueue(this, args);
        public void Dispose()
        {
            Character.Events.OnQuestProgressStat -= Forward;
            Character.Events.OnLaborPower -= Forward;
            Character.Events.OnZoneKill -= Forward;
            Character.Events.OnMonsterGroupHunt -= Forward;
            Character.Events.OnItemUse -= Forward;
            Character.Events.OnQuestComplete -= Forward;
        }
    }
}

public static class ExpeditionPublicAssignmentServices
{
    private static ExpeditionPublicAssignmentService _service;
    public static void Set(ExpeditionPublicAssignmentService service) => _service = service;
    public static void Clear(ExpeditionPublicAssignmentService service)
    {
        if (ReferenceEquals(_service, service))
            _service = null;
    }
    public static bool TryGet(out ExpeditionPublicAssignmentService service) { service = _service; return service != null; }
}
