using System.Collections.Concurrent;
using System.Text;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions.Activities;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;
using Microsoft.Extensions.DependencyInjection;
using NLog;

namespace AAEmu.Game.Core.Managers;

public sealed class ExpeditionActivityService(IExpeditionActivityRepository repository, IWorldManager worldManager,
    ExpeditionManager expeditionManager, IItemManager itemManager,
    IExpeditionActivityConnectionFactory connections, IFactionManager factionManager, TimeProvider timeProvider = null,
    INpcManager npcManager = null)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public const int MaximumSummonTargets = 50;
    public const int MaximumActivityHistoryRows = 50;
    public const int MaximumInstanceHistoryMembers = 50;
    public const int MaximumPortalNameLength = 128;
    // The server-side deadline is not known. The shipped summon dialog closes after 60 seconds;
    // matching it here prevents stale destinations and retained sessions after the UI can no longer reply.
    public static readonly TimeSpan SummonLifetime = TimeSpan.FromSeconds(60);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly INpcManager _npcManager = npcManager;
    private readonly ConcurrentDictionary<uint, PendingExpeditionSummon> _pendingSummons = [];

    public IReadOnlyList<uint> RequestSummons(Character summoner, IEnumerable<string> requestedNames)
    {
        PurgeExpiredSummons();
        using var persistenceOperation = PersistenceOperationScope.Enter();
        var expedition = summoner?.Expedition;
        if (expedition == null || summoner.Inventory == null)
            return [];

        Character[] targets;
        ItemConsumptionPublication publication;
        PendingDestination destination;
        lock (expedition.SyncRoot)
        lock (summoner.Inventory.MutationSyncRoot)
        {
            if (summoner.Connection?.ActiveChar != summoner || worldManager.GetCharacterById(summoner.Id) != summoner ||
                !ReferenceEquals(summoner.Expedition, expedition) || expedition.GetMember(summoner) == null)
                return [];

            var limit = ExpeditionLevelGameData.Instance.GetLevel(expedition.Level)?.SummonLimit ?? 0;
            var summonItemIdValue = expeditionManager.GetContentConfig("expedition_summon_item");
            if (limit <= 0 || summonItemIdValue is <= 0 or > uint.MaxValue)
                return [];
            var summonItemId = (uint)summonItemIdValue;

            targets = requestedNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Min(MaximumSummonTargets, limit))
                .Select(worldManager.GetCharacter)
                .Where(target => target != null && target.Connection?.ActiveChar == target && target.Id != summoner.Id &&
                                 ReferenceEquals(target.Expedition, expedition) && expedition.GetMember(target) != null)
                .DistinctBy(target => target.Id)
                .ToArray();
            if (targets.Length == 0 ||
                !summoner.Inventory.TryPlanBagConsumption(summonItemId, 1, out var consumption))
                return [];

            var snapshots = consumption.CapturePersistenceSnapshots(itemManager);
            using var connection = connections.Open();
            using var transaction = connection.BeginTransaction();
            itemManager.PersistSnapshots(connection, transaction, snapshots);
            transaction.Commit();

            publication = consumption.ApplyCommitted(ItemTaskType.SkillEffectConsumption);
            var position = summoner.Transform.World.Position;
            destination = new(summoner.Transform.ZoneId, summoner.ParentWorld, position.X, position.Y, position.Z,
                summoner.Transform.World.Rotation.Z);
            var expiresAt = _timeProvider.GetUtcNow() + SummonLifetime;
            foreach (var target in targets)
                _pendingSummons[target.Id] = new(target, summoner, summoner.Name, (uint)expedition.Id, destination,
                    expiresAt, false);

            try
            {
                publication.PublishPackets();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to publish the committed expedition summon item debit for {0}.",
                    summoner.Id);
            }
        }
        try
        {
            publication.PublishCallbacks();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to publish expedition summon item callbacks for {0}.", summoner.Id);
        }

        foreach (var target in targets)
        {
            target.SendPacket(new SCExpeditionSummonSuggestPacket(
                summoner.Name, destination.ZoneId, destination.X, destination.Y, destination.Z));
        }

        var ids = targets.Select(target => target.Id).ToArray();
        summoner.SendPacket(new SCExpeditionSummonGetPacket(ids));
        return ids;
    }

    public bool ReplyToSummon(Character recipient, bool accepted, string summonerName)
    {
        PurgeExpiredSummons();
        using var persistenceOperation = PersistenceOperationScope.Enter();
        if (recipient == null || !_pendingSummons.TryGetValue(recipient.Id, out var pending) ||
            !ReferenceEquals(recipient, pending.Recipient) ||
            !string.Equals(pending.SummonerName, summonerName, StringComparison.Ordinal))
            return false;
        if (!accepted)
            return TryRemovePending(new(recipient.Id, pending));

        var summoner = pending.Summoner;
        var expedition = recipient.Expedition;
        if (expedition == null)
            return false;
        lock (expedition.SyncRoot)
        {
            if (summoner.Connection?.ActiveChar != summoner || recipient.Connection?.ActiveChar != recipient ||
                worldManager.GetCharacterById(summoner.Id) != summoner ||
                worldManager.GetCharacterById(recipient.Id) != recipient ||
                !ReferenceEquals(recipient.Expedition, expedition) ||
                !ReferenceEquals(expedition, summoner.Expedition) || expedition.GetMember(recipient) == null ||
                expedition.GetMember(summoner) == null || (uint)expedition.Id != pending.ExpeditionId ||
                pending.Accepted || recipient.IsInBattle ||
                recipient.Buffs.CheckBuffTag((uint)BuffConstants.TagOverburdened))
                return false;

            if (!_pendingSummons.TryUpdate(recipient.Id, pending with { Accepted = true }, pending))
                return false;
            recipient.SendPacket(new SCExpeditionSummonPacket());
            return true;
        }
    }

    public bool CompleteSummon(Character recipient)
    {
        PurgeExpiredSummons();
        using var persistenceOperation = PersistenceOperationScope.Enter();
        if (recipient == null || !_pendingSummons.TryGetValue(recipient.Id, out var pending) || !pending.Accepted ||
            !ReferenceEquals(recipient, pending.Recipient))
            return false;

        var summoner = pending.Summoner;
        var expedition = recipient.Expedition;
        if (expedition == null)
            return false;
        lock (expedition.SyncRoot)
        {
            if (summoner.Connection?.ActiveChar != summoner || recipient.Connection?.ActiveChar != recipient ||
                worldManager.GetCharacterById(summoner.Id) != summoner ||
                worldManager.GetCharacterById(recipient.Id) != recipient ||
                !ReferenceEquals(recipient.Expedition, expedition) ||
                !ReferenceEquals(expedition, summoner.Expedition) || expedition.GetMember(recipient) == null ||
                expedition.GetMember(summoner) == null || (uint)expedition.Id != pending.ExpeditionId)
                return false;

            if (!TryRemovePending(new KeyValuePair<uint, PendingExpeditionSummon>(recipient.Id, pending)))
                return false;

            return TryTeleport(recipient, pending.Destination.ZoneId, pending.Destination.World,
                pending.Destination.X, pending.Destination.Y, pending.Destination.Z, pending.Destination.Yaw,
                TeleportReason.ExpeditionSummon);
        }
    }

    public void OnCharacterLogout(Character character)
    {
        if (character == null)
            return;
        foreach (var entry in _pendingSummons)
        {
            if (ReferenceEquals(entry.Value.Recipient, character) || ReferenceEquals(entry.Value.Summoner, character))
                TryRemovePending(entry);
        }
    }

    public IReadOnlyList<ExpeditionPortalPoint> GetPortals(Character character)
    {
        var expedition = character?.Expedition;
        if (expedition == null)
            return [];
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
            if (!IsCurrentMember(character, expedition))
                return [];
            return repository.GetPortals((uint)expedition.Id);
        }
    }

    public void SendPortals(Character character)
    {
        var expedition = character?.Expedition;
        if (expedition == null)
            return;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
            if (!IsCurrentMember(character, expedition))
                return;
            character.SendPacket(new SCExpeditionPortalsPacket(
                (uint)expedition.Id, repository.GetPortals((uint)expedition.Id)));
        }
    }

    public ExpeditionPortalPoint SavePortal(Character actor, SkillObjectUnk2 portalObject)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        var expedition = actor?.Expedition;
        if (expedition == null || portalObject == null || !ValidPortalName(portalObject.Name))
            return null;

        lock (expedition.SyncRoot)
        {
            if (!CanManage(actor, expedition))
                return null;

            if (portalObject.Id != 0)
            {
                if (portalObject.Id < 0)
                    return null;
                var portalId = (uint)portalObject.Id;
                if (!repository.RenamePortal((uint)expedition.Id, portalId, portalObject.Name))
                    return null;
                var renamed = repository.GetPortals((uint)expedition.Id).FirstOrDefault(x => x.Id == portalId);
                if (renamed != null)
                    expedition.SendPacket(new SCExpeditionPortalSavedPacket((uint)expedition.Id, renamed), worldManager);
                return renamed;
            }

            var limit = ExpeditionLevelGameData.Instance.GetLevel(expedition.Level)?.PortalPointLimit ?? 0;
            var position = actor.Transform.World.Position;
            var portal = new ExpeditionPortalPoint
            {
                ExpeditionId = (uint)expedition.Id,
                Name = portalObject.Name,
                ZoneId = actor.Transform.ZoneId,
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                ZRot = actor.Transform.World.Rotation.Z
            };
            if (!repository.TryAddPortal(portal, limit))
                return null;
            expedition.SendPacket(new SCExpeditionPortalSavedPacket((uint)expedition.Id, portal), worldManager);
            return portal;
        }
    }

    public bool DeletePortal(Character actor, uint portalId)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        var expedition = actor?.Expedition;
        if (expedition == null)
            return false;
        lock (expedition.SyncRoot)
        {
            if (!CanManage(actor, expedition) || !repository.DeletePortal((uint)expedition.Id, portalId))
                return false;
            expedition.SendPacket(new SCDeleteExpeditionPortalPacket(0, portalId), worldManager);
            return true;
        }
    }

    public bool TeleportToPortal(Character actor, uint portalId)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        var expedition = actor?.Expedition;
        if (expedition == null)
            return false;
        lock (expedition.SyncRoot)
        {
            if (actor.Connection?.ActiveChar != actor || worldManager.GetCharacterById(actor.Id) != actor ||
                !ReferenceEquals(actor.Expedition, expedition) || expedition.GetMember(actor) == null)
                return false;
            var portal = repository.GetPortals((uint)expedition.Id).FirstOrDefault(x => x.Id == portalId);
            if (portal == null)
                return false;
            var worldTemplate = worldManager.GetWorldTemplateByZoneKey(portal.ZoneId);
            var destinationWorld = actor.ParentWorld?.Template?.Id == worldTemplate?.Id
                ? actor.ParentWorld
                : worldManager.GetWorlds().FirstOrDefault(x =>
                    x.Template.Id == worldTemplate?.Id && x.DungeonInstance == null);
            return TryTeleport(actor, portal.ZoneId, destinationWorld, portal.X, portal.Y, portal.Z,
                portal.ZRot, TeleportReason.Portal);
        }
    }

    public bool ChangeSponsor(Character actor, uint expectedSponsorId, uint sponsorId)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        var expedition = actor?.Expedition;
        if (expedition == null)
            return false;
        lock (expedition.SyncRoot)
        {
            var member = expedition.GetMember(actor);
            if (!IsCurrentMember(actor, expedition) || expedition.OwnerId != actor.Id ||
                member?.Role != byte.MaxValue || (uint)expedition.MotherId != expectedSponsorId ||
                expectedSponsorId == sponsorId)
                return false;

            var requestedSponsor = factionManager.GetFaction((FactionsEnum)sponsorId);
            if (requestedSponsor is not { ShowCreateExpedition: true } ||
                requestedSponsor.MotherId != expeditionManager.GetAllianceId(expedition))
                return false;

            using var connection = connections.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE expeditions SET mother=@sponsor WHERE id=@id AND mother=@expected";
            command.Parameters.AddWithValue("@sponsor", sponsorId);
            command.Parameters.AddWithValue("@id", expedition.Id);
            command.Parameters.AddWithValue("@expected", expectedSponsorId);
            if (command.ExecuteNonQuery() != 1)
                return false;

            expedition.MotherId = (FactionsEnum)sponsorId;
            expedition.SendPacket(new SCExpeditionSponsorChangedPacket(expedition, true), worldManager);
            return true;
        }
    }

    public ExpeditionInstanceHistory RecordInstanceResult(uint expeditionId, uint instanceRankDetailId, uint instanceId,
        uint score, ExpeditionInstancePlayResult playResult,
        IReadOnlyList<ExpeditionInstanceHistoryMember> members, DateTime recordedAt)
    {
        if (expeditionId == 0 || instanceRankDetailId == 0 || instanceId == 0 || members is not { Count: > 0 })
            return null;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        var expedition = expeditionManager.GetExpedition((FactionsEnum)expeditionId);
        if (expedition == null)
            return null;
        lock (expedition.SyncRoot)
        {
            var validMembers = members
                .Where(candidate => expedition.Members.Any(member => member.CharacterId == candidate.CharacterId))
                .DistinctBy(candidate => candidate.CharacterId)
                .Take(MaximumInstanceHistoryMembers)
                .ToArray();
            if (validMembers.Length == 0)
                return null;
            var history = new ExpeditionInstanceHistory
            {
                InstanceRankDetailId = instanceRankDetailId,
                InstanceId = instanceId,
                Score = score,
                PlayResult = playResult,
                RecordedAt = recordedAt,
                Members = validMembers
            };
            repository.AddInstanceHistory(expeditionId, history);
            expedition.SendPacket(new SCExpeditionInstanceHistoryInfoListPacket(true, expeditionId,
                repository.GetInstanceHistories(expeditionId,
                    SCExpeditionInstanceHistoryInfoListPacket.MaximumHistories)), worldManager);
            return history;
        }
    }

    public void RecordBuffPurchase(uint expeditionId, string memberName, uint contributionCost, uint buffId,
        byte targetGrade, DateTime usedAt, MySql.Data.MySqlClient.MySqlConnection connection,
        MySql.Data.MySqlClient.MySqlTransaction transaction) =>
        repository.AddManagementHistory(expeditionId,
            new ExpeditionManagementHistory(memberName, 1, contributionCost, usedAt, buffId, targetGrade),
            connection, transaction);

    /// <summary>
    /// Buys goods from a contribution-point merchant with the member debit, purchase limits, item
    /// rows, and guild shop history committed on one transaction.
    /// </summary>
    public bool TryPurchaseContributionGoods(Character character, MerchantGoods pack,
        IReadOnlyList<(MerchantGoodsItem Good, int Count)> purchases, out MerchantGoodsItem failedGood,
        out IReadOnlyDictionary<uint, MerchantPurchaseState> updatedPurchaseStates)
    {
        failedGood = null;
        updatedPurchaseStates = new Dictionary<uint, MerchantPurchaseState>();
        if (character == null || pack?.Kind != MerchantPackKind.ItemPoint || purchases is not { Count: > 0 } ||
            purchases.Any(purchase => purchase.Good == null || purchase.Count <= 0 ||
                                      purchase.Good.Cost <= 0 || purchase.Good.Currency != ShopCurrencyType.ItemPoint ||
                                      !ReferenceEquals(pack.GetItem(purchase.Good.ItemTemplateId, purchase.Good.Grade),
                                          purchase.Good)))
            return false;

        long totalCost;
        ItemAcquisitionRequest[] acquisitionRequests;
        try
        {
            totalCost = purchases.Sum(purchase => checked((long)purchase.Good.Cost * purchase.Count));
            acquisitionRequests = purchases
                .GroupBy(purchase => (purchase.Good.ItemTemplateId, purchase.Good.Grade))
                .Select(group => new ItemAcquisitionRequest(group.Key.ItemTemplateId,
                    checked((int)group.Sum(purchase => (long)purchase.Count)), group.Key.Grade))
                .ToArray();
        }
        catch (OverflowException)
        {
            return false;
        }
        if (totalCost <= 0 || totalCost > int.MaxValue)
            return false;

        ItemAcquisitionPublication publication = null;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        var expedition = character.Expedition;
        if (expedition == null || character.Inventory?.Bag == null)
            return false;
        lock (expedition.SyncRoot)
        {
            var member = expedition.GetMember(character);
            if (member == null)
                return false;
            lock (member)
            lock (character.Inventory.MutationSyncRoot)
            {
                if (!IsCurrentMember(character, expedition) || expedition.GetMember(character.Id) != member ||
                    member.ContributionPoint < (ulong)totalCost)
                    return false;

                using var acquisition = character.Inventory.TryPlanBagAcquisition(itemManager,
                    acquisitionRequests,
                    _timeProvider.GetUtcNow().UtcDateTime, out var plannedAcquisition)
                    ? plannedAcquisition
                    : null;
                if (acquisition == null)
                {
                    character.SendErrorMessage(ErrorMessageType.BagFull);
                    return false;
                }
                var snapshots = acquisition.CapturePersistenceSnapshots();
                var purchaseManager = _npcManager ?? NpcManager.Instance;
                using var reservation = purchaseManager.BeginMerchantPurchaseReservation(character.Id, purchases);
                try
                {
                    using var connection = connections.Open();
                    using var transaction = connection.BeginTransaction();
                    if (!reservation.TryPersist(connection, transaction, out failedGood))
                    {
                        transaction.Rollback();
                        return false;
                    }

                    using (var debit = connection.CreateCommand())
                    {
                        debit.Transaction = transaction;
                        debit.CommandText = "UPDATE expedition_members SET contribution_point=contribution_point-@cost " +
                                            "WHERE expedition_id=@expedition_id AND character_id=@character_id " +
                                            "AND contribution_point>=@cost";
                        debit.Parameters.AddWithValue("@cost", totalCost);
                        debit.Parameters.AddWithValue("@expedition_id", expedition.Id);
                        debit.Parameters.AddWithValue("@character_id", character.Id);
                        if (debit.ExecuteNonQuery() != 1)
                        {
                            transaction.Rollback();
                            return false;
                        }
                    }

                    itemManager.PersistSnapshots(connection, transaction, snapshots);
                    var purchasedAt = _timeProvider.GetUtcNow().UtcDateTime;
                    foreach (var purchase in purchases)
                    {
                        repository.AddShopHistory((uint)expedition.Id,
                            new ExpeditionShopHistory(member.Name, checked((int)purchase.Good.ItemTemplateId),
                                purchase.Count, checked((ulong)purchase.Good.Cost * (ulong)purchase.Count),
                                purchasedAt), connection, transaction);
                    }
                    transaction.Commit();
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Failed to commit expedition shop purchase for character {0}.",
                        character.Id);
                    return false;
                }

                acquisition.MarkCommitted();

                // The database transaction owns these values now. Publish every live authoritative
                // value before packet encoding or callbacks can throw; committed IDs are no longer
                // eligible for rollback cleanup.
                member.ContributionPoint -= (uint)totalCost;
                reservation.ApplyCommitted();
                updatedPurchaseStates = reservation.UpdatedStates;
                publication = acquisition.ApplyCommitted(ItemTaskType.StoreBuy);
                try
                {
                    publication.PublishPackets();
                    character.SendPacket(new SCAddContributionPointPacket(
                        unchecked((uint)-(int)totalCost), member.ContributionPoint));
                    expedition.SendPacket(new SCExpeditionMemberStatusChangedPacket(member, 0), worldManager);
                    expedition.SendDescriptor(worldManager);
                }
                catch (Exception exception)
                {
                    Logger.Error(exception,
                        "Failed to publish committed expedition shop purchase for character {0}.", character.Id);
                }
            }
        }

        try
        {
            publication.PublishCallbacks();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to publish expedition shop item callbacks for character {0}.",
                character.Id);
        }
        return true;
    }

    public void SendHistories(Character character, ExpeditionHistoryPage page)
    {
        var expedition = character?.Expedition;
        if (expedition == null)
            return;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
            if (!IsCurrentMember(character, expedition))
                return;
            var expeditionId = (uint)expedition.Id;
            switch (page)
            {
                case ExpeditionHistoryPage.Management:
                    character.SendPacket(new SCExpeditionManagementHistoriesPacket(
                        repository.GetManagementHistories(expeditionId, MaximumActivityHistoryRows)));
                    break;
                case ExpeditionHistoryPage.Shop:
                    character.SendPacket(new SCExpeditionShopHistoriesPacket(
                        repository.GetShopHistories(expeditionId, MaximumActivityHistoryRows)));
                    break;
                case ExpeditionHistoryPage.War:
                    character.SendPacket(new SCExpdWarHistoriesPacket(
                        repository.GetWarHistories(expeditionId, MaximumActivityHistoryRows)));
                    break;
                case ExpeditionHistoryPage.Instance:
                    character.SendPacket(new SCExpeditionInstanceHistoryInfoListPacket(true, expeditionId,
                        repository.GetInstanceHistories(expeditionId,
                            SCExpeditionInstanceHistoryInfoListPacket.MaximumHistories)));
                    break;
            }
        }
    }

    public void SendWarHistories(Character character)
    {
        var expedition = character?.Expedition;
        if (expedition == null)
            return;
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (expedition.SyncRoot)
        {
            if (!IsCurrentMember(character, expedition))
                return;
            character.SendPacket(new SCExpdWarHistoriesPacket(
                repository.GetWarHistories((uint)expedition.Id, MaximumActivityHistoryRows)));
        }
    }

    private bool IsCurrentMember(Character actor, AAEmu.Game.Models.Game.Expeditions.Expedition expedition) =>
        actor != null && expedition != null && !expedition.isDisbanded &&
        actor.Connection?.ActiveChar == actor && worldManager.GetCharacterById(actor.Id) == actor &&
        ReferenceEquals(actor.Expedition, expedition) && expedition.GetMember(actor) != null;

    private bool CanManage(Character actor, AAEmu.Game.Models.Game.Expeditions.Expedition expedition) =>
        IsCurrentMember(actor, expedition) && expedition.OwnerId == actor.Id;

    private static bool ValidPortalName(string name) =>
        !string.IsNullOrWhiteSpace(name) && Encoding.UTF8.GetByteCount(name) <= MaximumPortalNameLength;

    private bool TryTeleport(Character character, uint zoneId,
        AAEmu.Game.Models.Game.World.WorldInstance destinationWorld, float x, float y, float z, float yaw,
        TeleportReason reason)
    {
        if (destinationWorld == null || character.IsInBattle ||
            !worldManager.GetWorlds().Any(world => ReferenceEquals(world, destinationWorld)))
            return false;
        if (character.Buffs.CheckBuffTag((uint)BuffConstants.TagOverburdened))
        {
            character.SendErrorMessage(ErrorMessageType.CannotUsePortalWithBackpack);
            return false;
        }

        return SkillTeleportLanding.TryApplyToWorld(character, destinationWorld, zoneId, x, y, z, yaw, reason);
    }

    private sealed record PendingExpeditionSummon(Character Recipient, Character Summoner, string SummonerName,
        uint ExpeditionId, PendingDestination Destination, DateTimeOffset ExpiresAt, bool Accepted);
    private sealed record PendingDestination(uint ZoneId, AAEmu.Game.Models.Game.World.WorldInstance World,
        float X, float Y, float Z, float Yaw);

    private void PurgeExpiredSummons()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var entry in _pendingSummons)
            if (entry.Value.ExpiresAt <= now)
                TryRemovePending(entry);
    }

    private bool TryRemovePending(KeyValuePair<uint, PendingExpeditionSummon> entry) =>
        ((ICollection<KeyValuePair<uint, PendingExpeditionSummon>>)_pendingSummons).Remove(entry);
}

public static class ExpeditionActivityServices
{
    public static ExpeditionActivityService Get() =>
        SingletonContainer.ServiceProvider?.GetRequiredService<ExpeditionActivityService>()
        ?? throw new InvalidOperationException("Expedition activity service is unavailable.");

    public static bool TryGet(out ExpeditionActivityService service)
    {
        service = SingletonContainer.ServiceProvider?.GetService<ExpeditionActivityService>();
        return service != null;
    }
}
