using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Packets.G2C;
using System.Text;
using NLog;

namespace AAEmu.Game.Core.Managers;

public sealed class ExpeditionRecruitmentService(
    IExpeditionRecruitmentRepository repository,
    ExpeditionManager expeditionManager,
    IWorldManager worldManager,
    IExpeditionRecruitmentConnectionFactory connections,
    IExpeditionRecruitmentJoinCoordinator joinCoordinator,
    TimeProvider timeProvider = null)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public const int PageSize = 5; // shipped recruitment list inserts five rows per page

    public IReadOnlyList<ExpeditionRecruitment> GetActive(DateTime now) => repository.GetActive(now);

    public ExpeditionRecruitmentPage Search(Character viewer, ExpeditionRecruitmentQuery query, DateTime now)
    {
        if (!IsCurrentSession(viewer)) return new(0, PageSize, []);
        var applied = query.MineOnly
            ? repository.GetApplicationsForCharacter(viewer.Id, now).Select(x => x.ExpeditionId).ToHashSet()
            : null;
        var rows = repository.GetActive(now).Where(row =>
        {
            var expedition = expeditionManager.GetExpedition((FactionsEnum)row.ExpeditionId);
            if (expedition == null || expedition.isDisbanded) return false;
            if (viewer.Faction.MotherId != expedition.MotherId) return false;
            if (applied != null && !applied.Contains(row.ExpeditionId)) return false;
            if (query.MinimumLevel > 0 && expedition.Level < (uint)query.MinimumLevel) return false;
            if (query.MaximumLevel > 0 && expedition.Level > (uint)query.MaximumLevel) return false;
            if (!string.IsNullOrWhiteSpace(query.ExpeditionName) &&
                !expedition.Name.Contains(query.ExpeditionName, StringComparison.OrdinalIgnoreCase)) return false;
            return query.InterestMask == 0 || (row.InterestMask & query.InterestMask) != 0;
        }).ToList();
        rows = query.SortType switch
        {
            2 => rows.OrderByDescending(x => expeditionManager.GetExpedition((FactionsEnum)x.ExpeditionId)?.Members.Count ?? 0).ThenBy(x => x.ExpeditionId).ToList(),
            3 => rows.OrderByDescending(x => expeditionManager.GetExpedition((FactionsEnum)x.ExpeditionId)?.Level ?? 0).ThenBy(x => x.ExpeditionId).ToList(),
            4 => rows.OrderBy(x => expeditionManager.GetExpedition((FactionsEnum)x.ExpeditionId)?.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.ExpeditionId).ToList(),
            _ => rows
        };
        var page = Math.Max(1, (int)query.Page);
        return new(rows.Count, PageSize, rows.Skip((page - 1) * PageSize).Take(PageSize).ToArray());
    }

    public IReadOnlyList<ExpeditionRecruitmentApplication> GetMyApplications(Character character) =>
        IsCurrentSession(character) ? repository.GetApplicationsForCharacter(character.Id, UtcNow) : [];

    public IReadOnlyList<ExpeditionRecruitmentApplication> GetApplicants(Character actor)
    {
        var expedition = actor.Expedition;
        return IsCurrentSession(actor) && CanManage(actor, expedition)
            ? repository.GetApplicationsForExpedition((uint)expedition.Id, UtcNow)
            : [];
    }

    public IReadOnlyList<(ExpeditionRecruitmentApplication Application, ExpeditionJoinCandidate Character)> GetApplicantRows(Character actor)
    {
        var applications = GetApplicants(actor);
        if (applications.Count == 0) return [];
        var candidates = repository.GetCandidates(applications.Select(x => x.CharacterId).Distinct().ToArray());
        var rows = new List<(ExpeditionRecruitmentApplication, ExpeditionJoinCandidate)>();
        foreach (var application in applications)
        {
            if (candidates.TryGetValue(application.CharacterId, out var character)) rows.Add((application, character));
        }
        return rows;
    }

    public ExpeditionRecruitmentResult Register(Character actor, short interestMask, uint periodDays,
        string introduction, DateTime now)
    {
        var expedition = actor.Expedition;
        if (!IsCurrentSession(actor) || !CanManage(actor, expedition)) return ExpeditionRecruitmentResult.NotAuthorized;
        if (!ValidText(introduction) || !ExpeditionRecruitmentPolicy.IsValidInterestMask(interestMask) ||
            !ExpeditionRecruitmentPolicy.TryGetCost(expeditionManager, periodDays, out var cost))
            return ExpeditionRecruitmentResult.InvalidRequest;

        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        lock (actor.WalletSyncRoot)
        {
            if (!IsCurrentSession(actor) || !CanManage(actor, expedition)) return ExpeditionRecruitmentResult.NotAuthorized;
            if (actor.Money < cost)
                return ExpeditionRecruitmentResult.NotEnoughMoney;
            using var connection = connections.Open();
            using var transaction = connection.BeginTransaction();
            repository.Upsert(new((uint)expedition.Id, interestMask, introduction, now,
                now.AddDays(periodDays)), connection, transaction);
            if (!repository.TryDebitMoney(actor.Id, cost, connection, transaction))
                return Rollback(transaction, ExpeditionRecruitmentResult.NotEnoughMoney);
            transaction.Commit();
            actor.Money -= cost;
            try
            {
                actor.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.ExpeditionCreation,
                    [new MoneyChange(-cost)], []));
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, $"Recruitment registration committed for character {actor.Id}, but its money notification failed");
            }
            return ExpeditionRecruitmentResult.Success;
        }
    }

    public ExpeditionRecruitmentResult DeleteRecruitment(Character actor)
    {
        var expedition = actor.Expedition;
        if (!IsCurrentSession(actor) || !CanManage(actor, expedition)) return ExpeditionRecruitmentResult.NotAuthorized;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
            if (!IsCurrentSession(actor) || !CanManage(actor, expedition)) return ExpeditionRecruitmentResult.NotAuthorized;
            using var connection = connections.Open();
            using var transaction = connection.BeginTransaction();
            var deleted = repository.DeleteRecruitment((uint)expedition.Id, connection, transaction);
            transaction.Commit();
            return deleted ? ExpeditionRecruitmentResult.Success : ExpeditionRecruitmentResult.NotFound;
        }
    }

    public ExpeditionRecruitmentResult Apply(Character character, uint expeditionId, string memo, DateTime now)
    {
        if (!IsCurrentSession(character)) return ExpeditionRecruitmentResult.NotAuthorized;
        if (character.Expedition != null) return ExpeditionRecruitmentResult.AlreadyMember;
        if (!ValidText(memo)) return ExpeditionRecruitmentResult.InvalidRequest;
        var expedition = expeditionManager.GetExpedition((FactionsEnum)expeditionId);
        if (expedition == null) return ExpeditionRecruitmentResult.NotFound;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
            if (!IsCurrentSession(character)) return ExpeditionRecruitmentResult.NotAuthorized;
            using var connection = connections.Open();
            using var transaction = connection.BeginTransaction();
            var candidate = repository.GetCandidateForUpdate(character.Id, connection, transaction);
            if (candidate == null || candidate.ExpeditionId != 0 || expedition.isDisbanded ||
                character.Expedition != null || character.Faction.MotherId != expedition.MotherId)
                return Rollback(transaction, ExpeditionRecruitmentResult.WrongFaction);
            var recruitment = repository.GetForUpdate(expeditionId, connection, transaction);
            if (recruitment == null || recruitment.ExpiresAt <= now) return Rollback(transaction, ExpeditionRecruitmentResult.NotFound);
            if (repository.ApplicationExistsForUpdate(expeditionId, character.Id, connection, transaction))
                return Rollback(transaction, ExpeditionRecruitmentResult.AlreadyExists);
            if (repository.CountApplicationsForUpdate(character.Id, now, connection, transaction) >= ExpeditionRecruitmentPolicy.MaximumApplications(expeditionManager))
                return Rollback(transaction, ExpeditionRecruitmentResult.ApplicationLimit);
            repository.AddApplication(new(expeditionId, character.Id, memo, now), connection, transaction);
            transaction.Commit();
            return ExpeditionRecruitmentResult.Success;
        }
    }

    public ExpeditionRecruitmentResult Withdraw(Character character, uint expeditionId)
    {
        if (!IsCurrentSession(character)) return ExpeditionRecruitmentResult.NotAuthorized;
        var expedition = expeditionManager.GetExpedition((FactionsEnum)expeditionId);
        if (expedition == null) return ExpeditionRecruitmentResult.NotFound;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
            if (!IsCurrentSession(character)) return ExpeditionRecruitmentResult.NotAuthorized;
            using var connection = connections.Open();
            using var transaction = connection.BeginTransaction();
            repository.GetCandidateForUpdate(character.Id, connection, transaction);
            var deleted = repository.DeleteApplication(expeditionId, character.Id, connection, transaction);
            transaction.Commit();
            return deleted ? ExpeditionRecruitmentResult.Success : ExpeditionRecruitmentResult.NotFound;
        }
    }

    public void DeleteCharacterApplications(uint characterId)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        using var connection = connections.Open();
        using var transaction = connection.BeginTransaction();
        repository.GetCandidateForUpdate(characterId, connection, transaction);
        repository.DeleteApplicationsForCharacter(characterId, connection, transaction);
        transaction.Commit();
    }

    public ExpeditionRecruitmentResult Reject(Character actor, uint characterId)
    {
        var expedition = actor.Expedition;
        if (!IsCurrentSession(actor) || !CanManage(actor, expedition)) return ExpeditionRecruitmentResult.NotAuthorized;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
            if (!IsCurrentSession(actor) || !CanManage(actor, expedition)) return ExpeditionRecruitmentResult.NotAuthorized;
            using var connection = connections.Open();
            using var transaction = connection.BeginTransaction();
            repository.GetCandidateForUpdate(characterId, connection, transaction);
            var deleted = repository.DeleteApplication((uint)expedition.Id, characterId, connection, transaction);
            transaction.Commit();
            return deleted ? ExpeditionRecruitmentResult.Success : ExpeditionRecruitmentResult.NotFound;
        }
    }

    public ExpeditionRecruitmentResult Accept(Character actor, uint characterId)
    {
        var expedition = actor.Expedition;
        if (!IsCurrentSession(actor) || !CanManage(actor, expedition)) return ExpeditionRecruitmentResult.NotAuthorized;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        var online = worldManager.GetCharacterById(characterId);
        if (online != null)
        {
            if (!joinCoordinator.TryBegin(actor, online, expedition, out var transition))
                return ExpeditionRecruitmentResult.NotAuthorized;
            using (transition)
            using (var connection = connections.Open())
            using (var transaction = connection.BeginTransaction())
            {
                if (!IsCurrentSession(actor) || !CanManage(actor, expedition))
                    return Rollback(transaction, ExpeditionRecruitmentResult.NotAuthorized);
                var locked = repository.GetCandidateForUpdate(characterId, connection, transaction);
                if (locked == null || locked.ExpeditionId != 0) return Rollback(transaction, ExpeditionRecruitmentResult.AlreadyMember);
                var recruitment = repository.GetForUpdate((uint)expedition.Id, connection, transaction);
                if (recruitment == null || recruitment.ExpiresAt <= UtcNow) return Rollback(transaction, ExpeditionRecruitmentResult.NotFound);
                if (!repository.ApplicationExistsForUpdate((uint)expedition.Id, characterId, connection, transaction)) return Rollback(transaction, ExpeditionRecruitmentResult.NotFound);
                transition.Persist(connection, transaction);
                repository.DeleteApplicationsForCharacter(characterId, connection, transaction);
                transaction.Commit();
                transition.Commit();
            }
        }
        else
        {
            var snapshot = repository.GetCandidate(characterId);
            if (snapshot == null || snapshot.ExpeditionId != 0) return ExpeditionRecruitmentResult.AlreadyMember;
            if (snapshot.ExpeditionRejoinUntil > UtcNowUnix) return ExpeditionRecruitmentResult.NotAuthorized;
            if (!joinCoordinator.TryBegin(actor, snapshot, expedition, out var transition))
                return ExpeditionRecruitmentResult.NotAuthorized;
            using (transition)
            using (var connection = connections.Open())
            using (var transaction = connection.BeginTransaction())
            {
                if (!IsCurrentSession(actor) || !CanManage(actor, expedition))
                    return Rollback(transaction, ExpeditionRecruitmentResult.NotAuthorized);
                var locked = repository.GetCandidateForUpdate(characterId, connection, transaction);
                if (locked == null || locked.ExpeditionId != 0) return Rollback(transaction, ExpeditionRecruitmentResult.AlreadyMember);
                if (locked.FactionId != snapshot.FactionId)
                    return Rollback(transaction, ExpeditionRecruitmentResult.WrongFaction);
                if (locked.ExpeditionRejoinUntil > UtcNowUnix)
                    return Rollback(transaction, ExpeditionRecruitmentResult.NotAuthorized);
                var recruitment = repository.GetForUpdate((uint)expedition.Id, connection, transaction);
                if (recruitment == null || recruitment.ExpiresAt <= UtcNow) return Rollback(transaction, ExpeditionRecruitmentResult.NotFound);
                if (!repository.ApplicationExistsForUpdate((uint)expedition.Id, characterId, connection, transaction)) return Rollback(transaction, ExpeditionRecruitmentResult.NotFound);
                transition.Persist(connection, transaction);
                repository.DeleteApplicationsForCharacter(characterId, connection, transaction);
                transaction.Commit();
                transition.Commit();
            }
        }
        return ExpeditionRecruitmentResult.Success;
    }

    private static bool CanManage(Character actor, Expedition expedition)
    {
        var member = expedition?.GetMember(actor);
        return ReferenceEquals(actor?.Expedition, expedition) && member != null &&
               expedition.GetPolicyByRole(member.Role)?.Invite == true;
    }

    private bool IsCurrentSession(Character character) => character?.Connection != null &&
                                                          ReferenceEquals(character.Connection.ActiveChar, character) &&
                                                          ReferenceEquals(worldManager.GetCharacterById(character.Id), character);

    private static bool ValidText(string text) => text != null &&
                                                  Encoding.UTF8.GetByteCount(text) <= ExpeditionRecruitmentPolicy.MaximumTextLength;

    private DateTime UtcNow => (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    private long UtcNowUnix => (timeProvider ?? TimeProvider.System).GetUtcNow().ToUnixTimeSeconds();

    private static ExpeditionRecruitmentResult Rollback(MySql.Data.MySqlClient.MySqlTransaction transaction,
        ExpeditionRecruitmentResult result)
    {
        transaction.Rollback();
        return result;
    }
}
