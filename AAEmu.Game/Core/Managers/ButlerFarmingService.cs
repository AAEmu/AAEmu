using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Trading;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public enum ButlerFarmingOperationFailure
{
    None,
    NotBound,
    Busy,
    InvalidContent,
    UnsupportedHarvestGrade,
    NoHarvestSlot,
    NoSpecialtyTradeSlot,
    DuplicateSpecialtyTrade,
    NotEnoughInputItem,
    NotEnoughLaborPower,
    NotEnoughGardenSize,
    NotEnoughProductionCost,
    JobNotFound,
    ConcurrentChange,
    PersistenceFailed
}

public readonly record struct ButlerHarvestAdmissionContext(
    ButlerTemplate ButlerTemplate,
    ButlerLevel ButlerLevel,
    ButlerHarvestGrade CurrentHarvestGrade,
    ButlerHarvestGrade RequiredHarvestGrade,
    ButlerHarvest Harvest,
    ButlerFarmingResources AvailableResources,
    uint LaborPowerPerUnit,
    ItemTaskType InputItemTaskType,
    bool HasActiveSpecialtyTradeJob);

public readonly record struct ButlerHarvestRegistrationResult(
    bool Success,
    ButlerFarmingOperationFailure Failure,
    ButlerHarvestJob Job,
    ButlerHarvestRegistrationCosts Costs);

public readonly record struct ButlerHarvestCancellationResult(
    bool Success,
    ButlerFarmingOperationFailure Failure,
    ButlerHarvestJob Job);

public readonly record struct ButlerGardenSlotExpansionContext(
    ButlerSlotExpansion Expansion,
    uint ButlerTemplateId,
    uint CurrentButlerLevel,
    ItemTaskType RequiredItemTaskType);

public readonly record struct ButlerGardenSlotExpansionResult(
    bool Success,
    ButlerFarmingOperationFailure Failure,
    uint ExpandedSlotCount);

public readonly record struct ButlerSpecialtyTradeSlotExpansionContext(
    ButlerSlotExpansion Expansion,
    uint ButlerTemplateId,
    uint CurrentButlerLevel,
    ItemTaskType RequiredItemTaskType);

public readonly record struct ButlerSpecialtyTradeRegistrationResult(
    bool Success,
    ButlerFarmingOperationFailure Failure,
    ButlerSpecialtyTradeJob Job,
    ButlerSpecialtyTradeCosts Costs);

public readonly record struct ButlerSpecialtyTradeCancellationResult(
    bool Success,
    ButlerFarmingOperationFailure Failure,
    ButlerSpecialtyTradeJob Job);

/// <summary>
/// Resolves admission data from immutable content and the already-locked farmhand aggregate.
/// Implementations must not acquire ButlerManager, House lifecycle, inventory, or database locks.
/// </summary>
public interface IButlerFarmingAdmissionResolver
{
    bool TryResolveHarvest(Character character, CharacterButler butler, uint staticHarvestId,
        out ButlerHarvestAdmissionContext context);
    bool TryResolveSpecialtyTrade(Character character, CharacterButler butler, uint specialtyType,
        short toZoneGroupType, out ButlerSpecialtyTradeAdmissionContext context);
    bool TryResolveSpecialtyTrade(Character character, CharacterButler butler, uint specialtyType,
        short toZoneGroupType, out ButlerSpecialtyTradeAdmissionContext context,
        out ButlerSpecialtyTradeRules.AdmissionFailure failure);
    bool TryResolveNextGardenSlotExpansion(Character character, CharacterButler butler,
        out ButlerGardenSlotExpansionContext context);
    bool TryResolveNextSpecialtyTradeSlotExpansion(Character character, CharacterButler butler,
        out ButlerSpecialtyTradeSlotExpansionContext context);
}

public interface IButlerSpecialtyTradeJobProcessor
{
    void ProcessDueSpecialtyTradeJobs();
}

internal enum DurableJobReadStatus
{
    Found,
    Missing,
    Unknown
}

/// <summary>
/// Database-first crop/livestock and specialty-trade registration, cancellation, settlement, and garden-slot expansion.
/// No live item or farmhand state changes until the caller-owned MySQL transaction commits.
/// </summary>
public sealed class ButlerFarmingService : IButlerSpecialtyTradeJobProcessor
{
    // The client applies this permanent-data key as harvestSlot in its SCButlerInfoUpdated
    // handler when updatedFlags carries the permanent-datas bit.
    public const sbyte HarvestSlotExpansionPermanentDataKey = 2;
    // The client's permanent-datas handler exposes the trade-slot expansion count under this key.
    public const sbyte SpecialtyTradeSlotExpansionPermanentDataKey = 3;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly IButlerManager _butlerManager;
    private readonly IButlerFarmingAdmissionResolver _admissionResolver;
    private readonly IButlerRepository _repository;
    private readonly IItemManager _itemManager;
    private readonly Func<MySqlConnection> _openConnection;
    private readonly IButlerSpecialtyTradeSettlement _specialtyTradeSettlement;
    private readonly IButlerSpecialtyTradePersistence _specialtyPersistence;
    private readonly Func<uint, Character> _characterResolver;
    private readonly Func<uint, uint, uint> _deliveryRoll;

    public ButlerFarmingService(
        IButlerManager butlerManager,
        IButlerFarmingAdmissionResolver admissionResolver,
        IButlerRepository repository,
        IItemManager itemManager)
        : this(butlerManager, admissionResolver, repository, itemManager, MySQL.CreateConnection)
    {
    }

    public ButlerFarmingService(
        IButlerManager butlerManager,
        IButlerFarmingAdmissionResolver admissionResolver,
        IButlerRepository repository,
        IItemManager itemManager,
        IButlerSpecialtyTradeSettlement specialtyTradeSettlement)
        : this(butlerManager, admissionResolver, repository, itemManager, MySQL.CreateConnection,
            null, specialtyTradeSettlement, null, null, null)
    {
    }

    public ButlerFarmingService(
        IButlerManager butlerManager,
        IButlerFarmingAdmissionResolver admissionResolver,
        IButlerRepository repository,
        IItemManager itemManager,
        IButlerSpecialtyTradeSettlement specialtyTradeSettlement,
        IButlerSpecialtyTradePersistence specialtyPersistence)
        : this(butlerManager, admissionResolver, repository, itemManager, MySQL.CreateConnection,
            null, specialtyTradeSettlement, null, null, specialtyPersistence)
    {
    }

    internal ButlerFarmingService(IButlerManager butlerManager, IButlerFarmingAdmissionResolver admissionResolver,
        IButlerRepository repository, IItemManager itemManager, Func<MySqlConnection> openConnection,
        Func<DateTime> utcNow = null)
        : this(butlerManager, admissionResolver, repository, itemManager, openConnection, utcNow,
            null, null, null, null)
    {
    }

    internal ButlerFarmingService(IButlerManager butlerManager, IButlerFarmingAdmissionResolver admissionResolver,
        IButlerRepository repository, IItemManager itemManager, Func<MySqlConnection> openConnection,
        Func<DateTime> utcNow, IButlerSpecialtyTradeSettlement specialtyTradeSettlement,
        Func<uint, Character> characterResolver, Func<uint, uint, uint> deliveryRoll,
        IButlerSpecialtyTradePersistence specialtyPersistence)
    {
        _butlerManager = butlerManager ?? throw new ArgumentNullException(nameof(butlerManager));
        _admissionResolver = admissionResolver ?? throw new ArgumentNullException(nameof(admissionResolver));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _itemManager = itemManager ?? throw new ArgumentNullException(nameof(itemManager));
        _openConnection = openConnection ?? throw new ArgumentNullException(nameof(openConnection));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _specialtyTradeSettlement = specialtyTradeSettlement;
        _specialtyPersistence = specialtyPersistence;
        _characterResolver = characterResolver ?? (id => WorldManager.Instance.GetCharacterById(id));
        _deliveryRoll = deliveryRoll ?? ((minimum, maximum) =>
            checked((uint)Random.Shared.NextInt64(minimum, checked((long)maximum + 1))));
    }

    private readonly Func<DateTime> _utcNow;

    public ButlerSpecialtyTradeRegistrationResult RegisterSpecialtyTrade(
        Character character, uint specialtyType, short toZoneGroupType)
    {
        if (character == null || specialtyType == 0 || toZoneGroupType <= 0)
            return FailedSpecialtyRegistration(ButlerFarmingOperationFailure.InvalidContent);

        var butler = _butlerManager.GetOrCreate(character.Id);
        var committedPublications = new List<ItemConsumptionPublication>();
        ButlerSpecialtyTradeRegistrationResult result;
        PersistenceGate.EnterOperation();
        try
        {
            lock (butler.OperationSyncRoot)
            lock (butler.SyncRoot)
            {
                if (!CanOperate(character, butler))
                    return FailedSpecialtyRegistration(ButlerFarmingOperationFailure.NotBound);
                if (!_admissionResolver.TryResolveSpecialtyTrade(character, butler, specialtyType,
                        toZoneGroupType, out var context, out var admissionFailure))
                    return FailedSpecialtyRegistration(admissionFailure switch
                    {
                        ButlerSpecialtyTradeRules.AdmissionFailure.NoSpecialtyTradeSlot =>
                            ButlerFarmingOperationFailure.NoSpecialtyTradeSlot,
                        ButlerSpecialtyTradeRules.AdmissionFailure.DuplicateSpecialtyTrade =>
                            ButlerFarmingOperationFailure.DuplicateSpecialtyTrade,
                        _ => ButlerFarmingOperationFailure.InvalidContent
                    });
                if (butler.LaborPower < (uint)context.CraftSkill.ConsumeLaborPower)
                    return FailedSpecialtyRegistration(ButlerFarmingOperationFailure.NotEnoughLaborPower);
                // Shipped ui_texts 11129/11224 charge an extra production cost when the farmhand is
                // already running another production function; the harvest side already mirrors this.
                if (!ButlerSpecialtyTradeRules.TryCalculateCosts(context, butler.LaborPower,
                        butler.HarvestJobs.Count > 0, out var costs))
                    return FailedSpecialtyRegistration(ButlerFarmingOperationFailure.InvalidContent);
                if (butler.RemainProductionCost < costs.TotalProductionCost)
                    return FailedSpecialtyRegistration(ButlerFarmingOperationFailure.NotEnoughProductionCost);
                uint deliveryTime;
                try
                {
                    if (!ButlerSpecialtyTradeRules.TryChooseDeliveryTime(context.Trade, _deliveryRoll,
                            out deliveryTime))
                        return FailedSpecialtyRegistration(ButlerFarmingOperationFailure.InvalidContent);
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Failed to choose specialty-trade delivery time for character {0}",
                        character.Id);
                    return FailedSpecialtyRegistration(ButlerFarmingOperationFailure.InvalidContent);
                }

                result = PersistSpecialtyTradeRegistration(character, butler, context, costs,
                    toZoneGroupType, deliveryTime, committedPublications);
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        foreach (var publication in committedPublications)
            publication.PublishCallbacks();
        return result;
    }

    private ButlerSpecialtyTradeRegistrationResult PersistSpecialtyTradeRegistration(
        Character character,
        CharacterButler butler,
        ButlerSpecialtyTradeAdmissionContext context,
        ButlerSpecialtyTradeCosts costs,
        short toZoneGroupType,
        uint deliveryTime,
        List<ItemConsumptionPublication> committedPublications)
    {
        if (!character.Inventory.TryAcquireFarmhandMutation(out var acquiredLease))
            return FailedSpecialtyRegistration(ButlerFarmingOperationFailure.Busy);
        using var inventoryLease = acquiredLease;

            if (!TryPlanSpecialtyMaterials(character, context.Materials, out var consumptions))
                return FailedSpecialtyRegistration(ButlerFarmingOperationFailure.NotEnoughInputItem);
            var snapshots = consumptions.SelectMany(consumption =>
                consumption.CapturePersistenceSnapshots(_itemManager)).ToArray();
            var createdTime = Helpers.UnixTime(_utcNow());
            var proposed = butler.Snapshot() with
            {
                LaborPower = butler.LaborPower - costs.LaborPower,
                RemainProductionCost = checked((ushort)(butler.RemainProductionCost - costs.TotalProductionCost))
            };
            var candidate = new ButlerSpecialtyTradeJobCandidate(
                context.Trade.NpcId,
                context.Trade.Id,
                checked((ushort)toZoneGroupType),
                context.Product.ItemId,
                createdTime,
                deliveryTime);

            ButlerSpecialtyTradeJob job = null;
            var committed = false;
            try
            {
                var durable = PersistSpecialtyTradeRows(proposed, candidate, snapshots);
                if (durable.Success && durable.JobId > 0)
                {
                    committed = true;
                    job = new ButlerSpecialtyTradeJob(durable.JobId, candidate.NpcId,
                        candidate.SpecialtyType, candidate.ToZoneGroupType, candidate.ProductItemId,
                        candidate.CreatedTime, candidate.DeliveryTime);
                }
                else if (durable.Ambiguous && ReadDurableSpecialtyJob(butler.CharacterId,
                             durable.JobId, out var recoveredJob) == DurableJobReadStatus.Found)
                {
                    committed = true;
                    job = recoveredJob;
                }
                else
                {
                    return FailedSpecialtyRegistration(ButlerFarmingOperationFailure.PersistenceFailed);
                }
                committedPublications.AddRange(consumptions.Select(consumption => consumption.ApplyCommitted(
                    ItemTaskType.RequestButlerSpecialtyTradeRegister)));
                butler.Apply(proposed);
                butler.ApplySpecialtyTradeJob(job);
                foreach (var publication in committedPublications)
                    publication.PublishPackets();
                PublishSpecialtyTradeUpdate(character, 1, 0, job.JobId, SpecialtyTradeData(job));
                PublishHarvestResourceUpdate(character, proposed);
                return new ButlerSpecialtyTradeRegistrationResult(true,
                    ButlerFarmingOperationFailure.None, job, costs);
            }
            catch (Exception exception) when (!committed)
            {
                Logger.Error(exception, "Failed to register farmhand specialty-trade job for character {0}",
                    character.Id);
                return FailedSpecialtyRegistration(ButlerFarmingOperationFailure.PersistenceFailed);
            }
            catch (Exception exception)
            {
                Logger.Fatal(exception, "Committed farmhand specialty-trade job {0} for character {1} could not be fully published",
                    job?.JobId, character.Id);
                if (job != null)
                {
                    try
                    {
                        foreach (var snapshot in snapshots)
                            _itemManager.ApplyCommittedSnapshot(snapshot);
                        butler.Apply(proposed);
                        butler.ApplySpecialtyTradeJob(job);
                    }
                    catch (Exception reconciliationException)
                    {
                        Logger.Error(reconciliationException,
                            "Failed to reconcile live specialty-trade job {0} for character {1}",
                            job.JobId, character.Id);
                    }
                }
                return new ButlerSpecialtyTradeRegistrationResult(true,
                    ButlerFarmingOperationFailure.None, job, costs);
            }
    }

    private DurableJobReadStatus ReadDurableSpecialtyJob(uint characterId, long jobId,
        out ButlerSpecialtyTradeJob job)
    {
        try
        {
            var found = _specialtyPersistence != null
                ? _specialtyPersistence.TryLoadSpecialtyTradeJob(characterId, jobId, out job)
                : _repository.TryLoadSpecialtyTradeJob(characterId, jobId, out job);
            return found ? DurableJobReadStatus.Found : DurableJobReadStatus.Missing;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to reconcile durable specialty-trade job {0} for character {1}",
                jobId, characterId);
            job = null;
            return DurableJobReadStatus.Unknown;
        }
    }

    private ButlerSpecialtyTradePersistResult PersistSpecialtyCancellation(uint characterId, long jobId)
    {
        if (_specialtyPersistence != null)
            return _specialtyPersistence.Cancel(characterId, jobId);

        using var connection = _openConnection();
        using var transaction = connection.BeginTransaction();
        if (!_repository.DeleteSpecialtyTradeJob(characterId, jobId, connection, transaction))
        {
            transaction.Rollback();
            return new ButlerSpecialtyTradePersistResult(false, false, jobId);
        }
        transaction.Commit();
        return new ButlerSpecialtyTradePersistResult(true, false, jobId);
    }

    private ButlerSpecialtyTradePersistResult PersistSpecialtyTradeRows(
        CharacterButlerRecord proposed,
        ButlerSpecialtyTradeJobCandidate candidate,
        IReadOnlyList<ItemPersistenceSnapshot> snapshots)
    {
        if (_specialtyPersistence != null)
            return _specialtyPersistence.Register(proposed, candidate, snapshots);

        using var connection = _openConnection();
        using var transaction = connection.BeginTransaction();
        _repository.Save(proposed, connection, transaction);
        _itemManager.PersistSnapshots(connection, transaction, snapshots);
        var jobId = _repository.InsertSpecialtyTradeJob(proposed.CharacterId, candidate,
            connection, transaction);
        transaction.Commit();
        return new ButlerSpecialtyTradePersistResult(true, false, jobId);
    }

    public ButlerSpecialtyTradeCancellationResult CancelSpecialtyTrade(Character character, long jobId)
    {
        if (character == null || jobId <= 0)
            return FailedSpecialtyCancellation(ButlerFarmingOperationFailure.InvalidContent);
        var butler = _butlerManager.GetOrCreate(character.Id);
        PersistenceGate.EnterOperation();
        try
        {
            lock (butler.OperationSyncRoot)
            lock (butler.SyncRoot)
            {
                if (!CanOperate(character, butler))
                    return FailedSpecialtyCancellation(ButlerFarmingOperationFailure.NotBound);
                if (!butler.SpecialtyTradeJobs.TryGetValue(jobId, out var job))
                    return FailedSpecialtyCancellation(ButlerFarmingOperationFailure.JobNotFound);
                try
                {
                    var durable = PersistSpecialtyCancellation(butler.CharacterId, jobId);
                    if (!durable.Success)
                    {
                        if (durable.Ambiguous && ReadDurableSpecialtyJob(butler.CharacterId,
                                jobId, out _) == DurableJobReadStatus.Missing)
                        {
                            butler.RemoveSpecialtyTradeJob(jobId);
                            PublishSpecialtyTradeUpdate(character, 3, 0, job.JobId, SpecialtyTradeData(job));
                            return new ButlerSpecialtyTradeCancellationResult(true,
                                ButlerFarmingOperationFailure.None, job);
                        }
                        return FailedSpecialtyCancellation(ButlerFarmingOperationFailure.ConcurrentChange);
                    }
                    butler.RemoveSpecialtyTradeJob(jobId);
                    PublishSpecialtyTradeUpdate(character, 3, 0, job.JobId, SpecialtyTradeData(job));
                    return new ButlerSpecialtyTradeCancellationResult(true,
                        ButlerFarmingOperationFailure.None, job);
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Failed to cancel farmhand specialty-trade job {0} for character {1}",
                        jobId, character.Id);
                    return FailedSpecialtyCancellation(ButlerFarmingOperationFailure.PersistenceFailed);
                }
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }
    }

    public void ProcessDueSpecialtyTradeJobs()
    {
        var now = Helpers.UnixTime(_utcNow());
        foreach (var butler in _butlerManager.SnapshotAll())
        {
            PersistenceGate.EnterOperation();
            try
            {
                lock (butler.OperationSyncRoot)
                lock (butler.SyncRoot)
                {
                    if (butler.IsDeleted || butler.HouseId == 0)
                        continue;
                    foreach (var job in butler.SnapshotSpecialtyTradeJobs())
                    {
                        if (!ButlerSpecialtyTradeRules.IsDue(job, now) ||
                            !butler.SpecialtyTradeJobs.TryGetValue(job.JobId, out var current) || current != job)
                            continue;
                        ProcessDueSpecialtyTradeJob(butler, job);
                    }
                }
            }
            finally
            {
                PersistenceGate.ExitOperation();
            }
        }
    }

    private void ProcessDueSpecialtyTradeJob(CharacterButler butler, ButlerSpecialtyTradeJob job)
    {
        if (job.ToZoneGroupType <= 0)
        {
            Logger.Error("Specialty-trade job {0} for character {1} has an invalid destination zone group.",
                job.JobId, butler.CharacterId);
            return;
        }
        SpecialtyMarketWrite market;
        try
        {
            if (_specialtyTradeSettlement == null || !_specialtyTradeSettlement.TryPrepare(
                    job.NpcId, job.ProductItemId, checked((uint)job.ToZoneGroupType), out market))
            {
                Logger.Error("Specialty-trade job {0} for character {1} has no valid market settlement.",
                    job.JobId, butler.CharacterId);
                return;
            }

            var durable = PersistSpecialtySettlement(butler.CharacterId, job.JobId, market);
            if (!durable.Success &&
                (!durable.Ambiguous ||
                 ReadDurableSpecialtyJob(butler.CharacterId, job.JobId, out _) != DurableJobReadStatus.Missing))
                return;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to settle farmhand specialty-trade job {0} for character {1}",
                job.JobId, butler.CharacterId);
            return;
        }

        try
        {
            _specialtyTradeSettlement.Commit(market);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to publish committed specialty-trade market state for job {0}",
                job.JobId);
        }
        butler.RemoveSpecialtyTradeJob(job.JobId);
        var owner = _characterResolver(butler.CharacterId);
        if (owner != null)
            PublishSpecialtyTradeUpdate(owner, 3, 0, job.JobId, SpecialtyTradeData(job));
    }

    private ButlerSpecialtyTradePersistResult PersistSpecialtySettlement(
        uint characterId,
        long jobId,
        SpecialtyMarketWrite market)
    {
        if (_specialtyPersistence != null)
            return _specialtyPersistence.Settle(characterId, jobId, market);

        using var connection = _openConnection();
        using var transaction = connection.BeginTransaction();
        if (!_repository.DeleteSpecialtyTradeJob(characterId, jobId, connection, transaction))
        {
            transaction.Rollback();
            return new ButlerSpecialtyTradePersistResult(false, false, jobId);
        }
        _specialtyTradeSettlement.Apply(market, connection, transaction);
        transaction.Commit();
        return new ButlerSpecialtyTradePersistResult(true, false, jobId);
    }

    private bool TryPlanSpecialtyMaterials(Character character,
        IReadOnlyList<ButlerSpecialtyTradeMaterialCost> materials, out List<ItemConsumptionPlan> consumptions)
    {
        consumptions = [];
        if (materials == null || materials.Count == 0)
            return false;
        foreach (var material in materials)
        {
            if (material.ItemId == 0 || material.Amount == 0 || material.Amount > int.MaxValue)
                return false;
            if (!character.Inventory.TryPlanBagConsumption(material.ItemId, checked((int)material.Amount),
                    out var consumption))
                return false;
            consumptions.Add(consumption);
        }
        return true;
    }

    private ButlerSpecialtyTradeDataWire SpecialtyTradeData(ButlerSpecialtyTradeJob job) =>
        new(job.SpecialtyType, job.ToZoneGroupType, checked((ulong)job.CreatedTime),
            checked((int)ButlerSpecialtyTradeRules.RemainingDeliverySeconds(job,
                Helpers.UnixTime(_utcNow()))));

    private void PublishSpecialtyTradeUpdate(Character character, byte jobKind, short error,
        long dbSpecialtyTradeId, ButlerSpecialtyTradeDataWire data) =>
        character?.SendPacket(new SCButlerSpecialtyTradeUpdatedPacket(jobKind, error, dbSpecialtyTradeId, data));

    public ButlerHarvestRegistrationResult RegisterHarvest(Character character, uint staticHarvestId, int amount)
    {
        if (character == null || staticHarvestId == 0)
            return FailedRegistration(ButlerFarmingOperationFailure.InvalidContent);

        var butler = _butlerManager.GetOrCreate(character.Id);
        ItemConsumptionPublication publication = null;
        ButlerHarvestRegistrationResult result;
        PersistenceGate.EnterOperation();
        try
        {
            lock (butler.OperationSyncRoot)
            lock (butler.SyncRoot)
            {
                if (!CanOperate(character, butler))
                    return FailedRegistration(ButlerFarmingOperationFailure.NotBound);
                if (!_admissionResolver.TryResolveHarvest(character, butler, staticHarvestId, out var context))
                    return FailedRegistration(ButlerFarmingOperationFailure.InvalidContent);
                if (!TryValidateRegistration(butler, context, amount, out var costs, out var failure))
                    return FailedRegistration(failure);
                if (!character.Inventory.TryAcquireFarmhandMutation(out var inventoryLease))
                    return FailedRegistration(ButlerFarmingOperationFailure.Busy);

                using (inventoryLease)
                {
                    if (!character.Inventory.TryPlanBagConsumption(
                            context.Harvest.ItemId!.Value, checked((int)costs.ItemCount), out var consumption))
                        return FailedRegistration(ButlerFarmingOperationFailure.NotEnoughInputItem);

                    var snapshots = consumption.CapturePersistenceSnapshots(_itemManager);
                    var proposed = butler.Snapshot() with
                    {
                        LaborPower = butler.LaborPower - costs.LaborPower,
                        RemainProductionCost = checked((ushort)(butler.RemainProductionCost - costs.TotalVigor))
                    };
                    var candidate = new ButlerHarvestJobCandidate(
                        context.Harvest.Id,
                        checked((ushort)amount),
                        checked((ushort)context.Harvest.RepeatCount!.Value),
                        costs.LaborPower,
                        Helpers.UnixTime(_utcNow()));

                    var committed = false;
                    try
                    {
                        using var connection = _openConnection();
                        using var transaction = connection.BeginTransaction();
                        _repository.Save(proposed, connection, transaction);
                        _itemManager.PersistSnapshots(connection, transaction, snapshots);
                        var jobId = _repository.InsertHarvestJob(
                            butler.CharacterId, candidate, connection, transaction);
                        transaction.Commit();
                        committed = true;

                        var job = new ButlerHarvestJob(
                            jobId,
                            candidate.StaticHarvestId,
                            candidate.RequestedAmount,
                            candidate.RemainingRepeatCount,
                            candidate.LaborPowerForExperience,
                            candidate.UpdateTime);
                        publication = consumption.ApplyCommitted(context.InputItemTaskType);
                        butler.Apply(proposed);
                        butler.ApplyHarvestJob(job);
                        publication.PublishPackets();
                        PublishHarvestUpdate(character, 1, job);
                        PublishHarvestResourceUpdate(character, proposed);
                        result = new ButlerHarvestRegistrationResult(true, ButlerFarmingOperationFailure.None,
                            job, costs);
                    }
                    catch (Exception ex) when (!committed)
                    {
                        Logger.Error(ex, "Failed to persist farmhand harvest {0} for character {1}",
                            context.Harvest.Id, butler.CharacterId);
                        return FailedRegistration(ButlerFarmingOperationFailure.PersistenceFailed);
                    }
                }
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        publication?.PublishCallbacks();
        return result;
    }

    public ButlerHarvestCancellationResult CancelHarvest(Character character, long jobId)
    {
        if (character == null || jobId <= 0)
            return new ButlerHarvestCancellationResult(false, ButlerFarmingOperationFailure.JobNotFound, null);

        var butler = _butlerManager.GetOrCreate(character.Id);
        PersistenceGate.EnterOperation();
        try
        {
            lock (butler.OperationSyncRoot)
            lock (butler.SyncRoot)
            {
                if (!CanOperate(character, butler))
                    return new ButlerHarvestCancellationResult(false, ButlerFarmingOperationFailure.NotBound, null);
                if (!butler.HarvestJobs.TryGetValue(jobId, out var job))
                    return new ButlerHarvestCancellationResult(false, ButlerFarmingOperationFailure.JobNotFound, null);

                var committed = false;
                try
                {
                    using var connection = _openConnection();
                    using var transaction = connection.BeginTransaction();
                    if (!_repository.DeleteHarvestJob(butler.CharacterId, jobId, connection, transaction))
                    {
                        transaction.Rollback();
                        return new ButlerHarvestCancellationResult(
                            false, ButlerFarmingOperationFailure.ConcurrentChange, null);
                    }

                    transaction.Commit();
                    committed = true;
                    if (!butler.RemoveHarvestJob(jobId))
                        throw new InvalidOperationException($"Committed farmhand job {jobId} vanished from live state.");
                    PublishHarvestUpdate(character, 3, job);
                    return new ButlerHarvestCancellationResult(true, ButlerFarmingOperationFailure.None, job);
                }
                catch (Exception ex) when (!committed)
                {
                    Logger.Error(ex, "Failed to cancel farmhand harvest job {0} for character {1}",
                        jobId, butler.CharacterId);
                    return new ButlerHarvestCancellationResult(
                        false, ButlerFarmingOperationFailure.PersistenceFailed, null);
                }
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }
    }

    public ButlerGardenSlotExpansionResult ExpandGardenSlots(Character character)
    {
        if (character == null)
            return FailedExpansion(ButlerFarmingOperationFailure.InvalidContent);

        var butler = _butlerManager.GetOrCreate(character.Id);
        ItemConsumptionPublication publication = null;
        ButlerGardenSlotExpansionResult result;
        PersistenceGate.EnterOperation();
        try
        {
            lock (butler.OperationSyncRoot)
            lock (butler.SyncRoot)
            {
                if (!CanOperate(character, butler))
                    return FailedExpansion(ButlerFarmingOperationFailure.NotBound);
                if (!_admissionResolver.TryResolveNextGardenSlotExpansion(character, butler, out var context))
                    return FailedExpansion(ButlerFarmingOperationFailure.InvalidContent);
                if (!TryValidateExpansion(butler, context, out var failure))
                    return FailedExpansion(failure);
                if (!character.Inventory.TryAcquireFarmhandMutation(out var inventoryLease))
                    return FailedExpansion(ButlerFarmingOperationFailure.Busy);

                using (inventoryLease)
                {
                    if (!character.Inventory.TryPlanBagConsumption(
                            context.Expansion.RequireItemId,
                            checked((int)context.Expansion.RequireItemCount),
                            out var consumption))
                        return FailedExpansion(ButlerFarmingOperationFailure.NotEnoughInputItem);

                    var snapshots = consumption.CapturePersistenceSnapshots(_itemManager);
                    var committed = false;
                    try
                    {
                        using var connection = _openConnection();
                        using var transaction = connection.BeginTransaction();
                        _repository.SavePermanentData(
                            butler.CharacterId,
                            HarvestSlotExpansionPermanentDataKey,
                            context.Expansion.TotalExpandSlotCount,
                            connection,
                            transaction);
                        _itemManager.PersistSnapshots(connection, transaction, snapshots);
                        transaction.Commit();
                        committed = true;

                        publication = consumption.ApplyCommitted(context.RequiredItemTaskType);
                        butler.ApplyPermanentData(
                            HarvestSlotExpansionPermanentDataKey, context.Expansion.TotalExpandSlotCount);
                        publication.PublishPackets();
                        PublishGardenSlotExpansion(character, context.Expansion.TotalExpandSlotCount);
                        result = new ButlerGardenSlotExpansionResult(
                            true, ButlerFarmingOperationFailure.None, context.Expansion.TotalExpandSlotCount);
                    }
                    catch (Exception ex) when (!committed)
                    {
                        Logger.Error(ex, "Failed to expand farmhand garden slots for character {0}",
                            butler.CharacterId);
                        return FailedExpansion(ButlerFarmingOperationFailure.PersistenceFailed);
                    }
                }
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        publication?.PublishCallbacks();
        return result;
    }

    public ButlerGardenSlotExpansionResult ExpandSpecialtyTradeSlots(Character character)
    {
        if (character == null)
            return FailedExpansion(ButlerFarmingOperationFailure.InvalidContent);

        var butler = _butlerManager.GetOrCreate(character.Id);
        ItemConsumptionPublication publication = null;
        ButlerGardenSlotExpansionResult result;
        PersistenceGate.EnterOperation();
        try
        {
            lock (butler.OperationSyncRoot)
            lock (butler.SyncRoot)
            {
                if (!CanOperate(character, butler))
                    return FailedExpansion(ButlerFarmingOperationFailure.NotBound);
                if (!_admissionResolver.TryResolveNextSpecialtyTradeSlotExpansion(character, butler,
                        out var context))
                    return FailedExpansion(ButlerFarmingOperationFailure.InvalidContent);
                if (!TryValidateSpecialtyTradeExpansion(butler, context, out var failure))
                    return FailedExpansion(failure);
                if (!character.Inventory.TryAcquireFarmhandMutation(out var inventoryLease))
                    return FailedExpansion(ButlerFarmingOperationFailure.Busy);

                using (inventoryLease)
                {
                    if (!character.Inventory.TryPlanBagConsumption(
                            context.Expansion.RequireItemId,
                            checked((int)context.Expansion.RequireItemCount),
                            out var consumption))
                        return FailedExpansion(ButlerFarmingOperationFailure.NotEnoughInputItem);

                    var snapshots = consumption.CapturePersistenceSnapshots(_itemManager);
                    var committed = false;
                    try
                    {
                        using var connection = _openConnection();
                        using var transaction = connection.BeginTransaction();
                        _repository.SavePermanentData(
                            butler.CharacterId,
                            SpecialtyTradeSlotExpansionPermanentDataKey,
                            context.Expansion.TotalExpandSlotCount,
                            connection,
                            transaction);
                        _itemManager.PersistSnapshots(connection, transaction, snapshots);
                        transaction.Commit();
                        committed = true;

                        publication = consumption.ApplyCommitted(context.RequiredItemTaskType);
                        butler.ApplyPermanentData(
                            SpecialtyTradeSlotExpansionPermanentDataKey,
                            context.Expansion.TotalExpandSlotCount);
                        publication.PublishPackets();
                        PublishSpecialtyTradeSlotExpansion(character,
                            context.Expansion.TotalExpandSlotCount);
                        result = new ButlerGardenSlotExpansionResult(
                            true, ButlerFarmingOperationFailure.None,
                            context.Expansion.TotalExpandSlotCount);
                    }
                    catch (Exception ex) when (!committed)
                    {
                        Logger.Error(ex, "Failed to expand farmhand specialty-trade slots for character {0}",
                            butler.CharacterId);
                        return FailedExpansion(ButlerFarmingOperationFailure.PersistenceFailed);
                    }
                }
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        publication?.PublishCallbacks();
        return result;
    }

    private static void PublishHarvestUpdate(Character character, byte jobKind, ButlerHarvestJob job)
    {
        character.SendPacket(new SCButlerHarvestUpdatedPacket(
            jobKind,
            (short)ErrorMessageType.NoErrorMessage,
            job.JobId,
            new ButlerHarvestDataWire(
                job.StaticHarvestId,
                checked((short)job.RemainingRepeatCount),
                checked((short)job.RequestedAmount),
                job.LaborPowerForExperience,
                job.UpdateTime)));
    }

    private static void PublishHarvestResourceUpdate(Character character, CharacterButlerRecord state)
    {
        character.SendPacket(new SCButlerInfoUpdatedPacket(
            (ushort)ErrorMessageType.NoErrorMessage,
            ButlerChargeService.LaborPowerAndProductionCostUpdatedFlags,
            false,
            false,
            Array.Empty<ButlerActabilityWire>(),
            new Dictionary<sbyte, ulong>(),
            state.LaborPower,
            state.LpChargedAmount,
            state.RemainProductionCost,
            string.Empty,
            new Dictionary<uint, uint>()));
    }

    private static void PublishSpecialtyTradeSlotExpansion(Character character, uint expandedSlotCount)
    {
        character.SendPacket(new SCButlerInfoUpdatedPacket(
            (ushort)ErrorMessageType.NoErrorMessage,
            ButlerChargeService.PermanentDatasUpdatedFlags,
            false,
            false,
            Array.Empty<ButlerActabilityWire>(),
            new Dictionary<sbyte, ulong>
            {
                [SpecialtyTradeSlotExpansionPermanentDataKey] = expandedSlotCount
            },
            0,
            0,
            0,
            string.Empty,
            new Dictionary<uint, uint>()));
    }

    private static void PublishGardenSlotExpansion(Character character, uint expandedSlotCount)
    {
        character.SendPacket(new SCButlerInfoUpdatedPacket(
            (ushort)ErrorMessageType.NoErrorMessage,
            ButlerChargeService.PermanentDatasUpdatedFlags,
            false,
            false,
            Array.Empty<ButlerActabilityWire>(),
            new Dictionary<sbyte, ulong>
            {
                [HarvestSlotExpansionPermanentDataKey] = expandedSlotCount
            },
            0,
            0,
            0,
            string.Empty,
            new Dictionary<uint, uint>()));
    }

    private static bool TryValidateRegistration(
        CharacterButler butler,
        ButlerHarvestAdmissionContext context,
        int amount,
        out ButlerHarvestRegistrationCosts costs,
        out ButlerFarmingOperationFailure failure)
    {
        costs = default;
        failure = ButlerFarmingOperationFailure.InvalidContent;
        if (context.ButlerTemplate == null || context.ButlerLevel == null ||
            context.CurrentHarvestGrade == null || context.RequiredHarvestGrade == null || context.Harvest == null ||
            context.ButlerTemplate.Id == 0 || context.ButlerLevel.ButlerId != context.ButlerTemplate.Id ||
            context.CurrentHarvestGrade.Id != context.ButlerLevel.ButlerHarvestGradeId ||
            context.RequiredHarvestGrade.Id != context.Harvest.ButlerHarvestGradeId ||
            context.InputItemTaskType == ItemTaskType.Invalid ||
            context.Harvest.RepeatCount is null or 0 || context.Harvest.RepeatCount > short.MaxValue)
            return false;

        var expandedSlots = butler.PermanentDatas.GetValueOrDefault(
            HarvestSlotExpansionPermanentDataKey);
        if (context.ButlerTemplate.DefaultGardenSlotCount is not > 0 ||
            expandedSlots > uint.MaxValue ||
            (ulong)context.ButlerTemplate.DefaultGardenSlotCount.Value + expandedSlots > int.MaxValue)
            return false;
        var harvestSlotCapacity = context.ButlerTemplate.DefaultGardenSlotCount.Value + (uint)expandedSlots;
        if ((uint)butler.HarvestJobs.Count >= harvestSlotCapacity)
        {
            failure = ButlerFarmingOperationFailure.NoHarvestSlot;
            return false;
        }

        if (context.CurrentHarvestGrade.Grade < context.RequiredHarvestGrade.Grade)
        {
            failure = ButlerFarmingOperationFailure.UnsupportedHarvestGrade;
            return false;
        }

        if (!ButlerFarmingRules.TryCalculateRegistrationCosts(
                context.Harvest,
                context.ButlerTemplate,
                context.LaborPowerPerUnit,
                amount,
                context.HasActiveSpecialtyTradeJob,
                out costs,
                out _))
            return false;

        if (context.AvailableResources.LaborPower < costs.LaborPower ||
            butler.LaborPower < costs.LaborPower)
        {
            failure = ButlerFarmingOperationFailure.NotEnoughLaborPower;
            return false;
        }

        var availableGardenSize = costs.IsUnderWater
            ? context.AvailableResources.WaterGardenSize
            : context.AvailableResources.LandGardenSize;
        if (availableGardenSize < costs.GardenSize)
        {
            failure = ButlerFarmingOperationFailure.NotEnoughGardenSize;
            return false;
        }

        if (context.AvailableResources.Vigor < costs.TotalVigor ||
            butler.RemainProductionCost < costs.TotalVigor)
        {
            failure = ButlerFarmingOperationFailure.NotEnoughProductionCost;
            return false;
        }

        failure = ButlerFarmingOperationFailure.None;
        return true;
    }

    private static bool TryValidateSpecialtyTradeExpansion(
        CharacterButler butler,
        ButlerSpecialtyTradeSlotExpansionContext context,
        out ButlerFarmingOperationFailure failure)
    {
        failure = ButlerFarmingOperationFailure.InvalidContent;
        if (context.Expansion == null || context.Expansion.ButlerId == 0 ||
            context.Expansion.ButlerId != context.ButlerTemplateId ||
            context.Expansion.Level > context.CurrentButlerLevel ||
            context.RequiredItemTaskType == ItemTaskType.Invalid ||
            context.Expansion.RequireItemId == 0 || context.Expansion.RequireItemCount == 0)
            return false;

        var current = butler.PermanentDatas.GetValueOrDefault(
            SpecialtyTradeSlotExpansionPermanentDataKey);
        if (current >= uint.MaxValue ||
            context.Expansion.TotalExpandSlotCount != checked((uint)current + 1))
            return false;

        failure = ButlerFarmingOperationFailure.None;
        return true;
    }

    private static bool TryValidateExpansion(
        CharacterButler butler,
        ButlerGardenSlotExpansionContext context,
        out ButlerFarmingOperationFailure failure)
    {
        failure = ButlerFarmingOperationFailure.InvalidContent;
        if (context.Expansion == null ||
            context.Expansion.ButlerId == 0 || context.Expansion.ButlerId != context.ButlerTemplateId ||
            context.Expansion.Level > context.CurrentButlerLevel ||
            context.RequiredItemTaskType == ItemTaskType.Invalid ||
            context.Expansion.RequireItemId == 0 || context.Expansion.RequireItemCount == 0)
            return false;

        var current = butler.PermanentDatas.GetValueOrDefault(HarvestSlotExpansionPermanentDataKey);
        if (current > uint.MaxValue)
            return false;
        var currentExpandedSlots = (uint)current;
        if (currentExpandedSlots == uint.MaxValue ||
            context.Expansion.TotalExpandSlotCount != currentExpandedSlots + 1)
            return false;

        failure = ButlerFarmingOperationFailure.None;
        return true;
    }

    private static bool CanOperate(Character character, CharacterButler butler) =>
        character != null && character.Id == butler.CharacterId && !butler.IsDeleted && butler.HouseId != 0;

    private static ButlerHarvestRegistrationResult FailedRegistration(ButlerFarmingOperationFailure failure) =>
        new(false, failure, null, default);

    private static ButlerGardenSlotExpansionResult FailedExpansion(ButlerFarmingOperationFailure failure) =>
        new(false, failure, 0);

    private static ButlerSpecialtyTradeRegistrationResult FailedSpecialtyRegistration(
        ButlerFarmingOperationFailure failure) => new(false, failure, null, default);

    private static ButlerSpecialtyTradeCancellationResult FailedSpecialtyCancellation(
        ButlerFarmingOperationFailure failure) => new(false, failure, null);
}
