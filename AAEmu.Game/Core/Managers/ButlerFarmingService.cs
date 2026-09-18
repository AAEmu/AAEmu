using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
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

/// <summary>
/// Resolves admission data from immutable content and the already-locked farmhand aggregate.
/// Implementations must not acquire ButlerManager, House lifecycle, inventory, or database locks.
/// </summary>
public interface IButlerFarmingAdmissionResolver
{
    bool TryResolveHarvest(Character character, CharacterButler butler, uint staticHarvestId,
        out ButlerHarvestAdmissionContext context);
    bool TryResolveNextGardenSlotExpansion(Character character, CharacterButler butler,
        out ButlerGardenSlotExpansionContext context);
}

/// <summary>
/// Database-first crop/livestock registration, cancellation, and garden-slot expansion.
/// No live item or farmhand state changes until the caller-owned MySQL transaction commits.
/// </summary>
public sealed class ButlerFarmingService
{
    // The client applies this permanent-data key as harvestSlot in its SCButlerInfoUpdated
    // handler when updatedFlags carries the permanent-datas bit.
    public const sbyte HarvestSlotExpansionPermanentDataKey = 2;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly IButlerManager _butlerManager;
    private readonly IButlerFarmingAdmissionResolver _admissionResolver;
    private readonly IButlerRepository _repository;
    private readonly IItemManager _itemManager;
    private readonly Func<MySqlConnection> _openConnection;

    public ButlerFarmingService(
        IButlerManager butlerManager,
        IButlerFarmingAdmissionResolver admissionResolver,
        IButlerRepository repository,
        IItemManager itemManager)
        : this(butlerManager, admissionResolver, repository, itemManager, MySQL.CreateConnection)
    {
    }

    internal ButlerFarmingService(IButlerManager butlerManager, IButlerFarmingAdmissionResolver admissionResolver,
        IButlerRepository repository, IItemManager itemManager, Func<MySqlConnection> openConnection,
        Func<DateTime> utcNow = null)
    {
        _butlerManager = butlerManager ?? throw new ArgumentNullException(nameof(butlerManager));
        _admissionResolver = admissionResolver ?? throw new ArgumentNullException(nameof(admissionResolver));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _itemManager = itemManager ?? throw new ArgumentNullException(nameof(itemManager));
        _openConnection = openConnection ?? throw new ArgumentNullException(nameof(openConnection));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    private readonly Func<DateTime> _utcNow;

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
}
