using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Account;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Packets.G2C;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public enum ButlerChargeOperationFailure
{
    None,
    NotBound,
    InvalidContent,
    InvalidSourceItem,
    Busy,
    NotEnoughPlayerLaborPower,
    ConcurrentChange,
    PersistenceFailed
}

public readonly record struct ButlerChargeContext(
    uint MaximumLaborPower,
    uint LaborPowerChargeRate,
    uint MaximumProductionCost,
    ButlerChargeContentConfig ContentConfig);

public readonly record struct ButlerLaborPowerChargeResult(
    bool Success,
    ButlerChargeOperationFailure Failure,
    ButlerChargeFailure RuleFailure,
    ButlerLaborPowerChargeQuote Quote);

public readonly record struct ButlerProductionCostChargeResult(
    bool Success,
    ButlerChargeOperationFailure Failure,
    ButlerChargeFailure RuleFailure,
    ButlerProductionCostChargeQuote Quote);

public readonly record struct ButlerExperienceGrantResult(
    bool Success,
    ButlerChargeOperationFailure Failure,
    ulong PreviousExperience,
    ulong NewExperience);

public readonly record struct ButlerQuotaRefreshResult(
    bool Success,
    ButlerChargeOperationFailure Failure,
    bool Changed,
    bool LaborPowerCounterChanged,
    bool ProductionCostCountersChanged);

public interface IButlerChargeService
{
    ButlerLaborPowerChargeResult ChargeLaborPower(Character character, uint amount);
    ButlerProductionCostChargeResult ChargeFreeProductionCost(Character character);
    ButlerProductionCostChargeResult ChargePaidProductionCost(
        Character character, ulong sourceItemId, uint resolvedSpecialEffectValue, int consumeCount);
    ButlerExperienceGrantResult AddExperience(
        Character character, ulong sourceItemId, int resolvedSpecialEffectValue, int consumeCount);
    ButlerQuotaRefreshResult RefreshQuotaPeriods(CharacterButler butler);
    ButlerQuotaRefreshResult RefreshQuotaPeriods(Character character);
}

/// <summary>
/// Resolves the unique Butler template and current level while the service holds the farmhand
/// operation and state locks. It must not acquire manager, House,
/// inventory, account, or database locks.
/// </summary>
public interface IButlerChargeContextResolver
{
    bool TryResolve(Character character, CharacterButler butler, out ButlerChargeContext context);
    bool TryResolve(CharacterButler butler, out ButlerChargeContext context);
}

/// <summary>Database-first farmhand LP and production-cost charging.</summary>
public sealed class ButlerChargeService : IButlerChargeService
{
    // SCButlerInfoUpdated serializer FUN_39C885B0 and applier FUN_390CEF60.
    internal const short PermanentDatasUpdatedFlags = 0x02;
    internal const short LaborPowerUpdatedFlags = 0x04;
    internal const short LaborPowerAndPermanentDatasUpdatedFlags = 0x06;
    internal const short LaborPowerAndProductionCostUpdatedFlags = 0x0C;
    internal const short ProductionCostAndPermanentDatasUpdatedFlags = 0x0A;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly IButlerManager _butlerManager;
    private readonly IButlerChargeContextResolver _contextResolver;
    private readonly IButlerRepository _repository;
    private readonly IAccountManager _accountManager;
    private readonly IItemManager _itemManager;
    private readonly Func<MySqlConnection> _openConnection;
    private readonly Func<DateTime> _utcNow;

    public ButlerChargeService(
        IButlerManager butlerManager,
        IButlerChargeContextResolver contextResolver,
        IButlerRepository repository,
        IAccountManager accountManager,
        IItemManager itemManager)
        : this(butlerManager, contextResolver, repository, accountManager, itemManager,
            MySQL.CreateConnection)
    {
    }

    internal ButlerChargeService(
        IButlerManager butlerManager,
        IButlerChargeContextResolver contextResolver,
        IButlerRepository repository,
        IAccountManager accountManager,
        IItemManager itemManager,
        Func<MySqlConnection> openConnection,
        Func<DateTime> utcNow = null)
    {
        _butlerManager = butlerManager ?? throw new ArgumentNullException(nameof(butlerManager));
        _contextResolver = contextResolver ?? throw new ArgumentNullException(nameof(contextResolver));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
        _itemManager = itemManager ?? throw new ArgumentNullException(nameof(itemManager));
        _openConnection = openConnection ?? throw new ArgumentNullException(nameof(openConnection));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public ButlerLaborPowerChargeResult ChargeLaborPower(Character character, uint amount)
    {
        if (character == null || amount > int.MaxValue)
            return FailedLabor(ButlerChargeOperationFailure.InvalidContent);

        var butler = _butlerManager.GetOrCreate(character.Id);
        AccountLaborDebitPublication publication = null;
        ButlerLaborPowerChargeResult result;
        PersistenceGate.EnterOperation();
        try
        {
            result = _accountManager.WithAccountLock(character.AccountId, () =>
            {
                lock (butler.OperationSyncRoot)
                lock (butler.SyncRoot)
                {
                    if (!CanOperate(character, butler))
                        return FailedLabor(ButlerChargeOperationFailure.NotBound);
                    if (!_contextResolver.TryResolve(character, butler, out var context) ||
                        !ValidContext(context))
                        return FailedLabor(ButlerChargeOperationFailure.InvalidContent);
                    var nowUtc = NormalizeUtc(_utcNow());
                    if (!TryResolveDailyLaborCounter(
                            butler, nowUtc, out var dailyChargedAmount, out var currentPeriod))
                        return FailedLabor(ButlerChargeOperationFailure.InvalidContent);

                    var request = new ButlerLaborPowerChargeRequest(
                        amount,
                        butler.LaborPower,
                        context.MaximumLaborPower,
                        dailyChargedAmount,
                        context.ContentConfig.LpDailyChargeAmountLimit,
                        context.ContentConfig.LpChargeMinimum,
                        context.LaborPowerChargeRate);
                    if (!ButlerChargeRules.TryQuoteLaborPowerCharge(
                            request, out var quote, out var ruleFailure))
                        return new ButlerLaborPowerChargeResult(
                            false, ButlerChargeOperationFailure.None, ruleFailure, default);

                    if (!AccountLaborDebitRules.TryCreate(
                            character.AccountId,
                            new AccountLaborBalance(character.LaborPower, character.LocalLaborPower),
                            checked((int)quote.PlayerLaborPowerDebit),
                            out var debit))
                        return FailedLabor(ButlerChargeOperationFailure.NotEnoughPlayerLaborPower);

                    var proposed = butler.Snapshot() with
                    {
                        LaborPower = quote.NewButlerLaborPower,
                        LpChargedAmount = quote.NewDailyChargedAmount,
                        LpChargeResetTime = currentPeriod
                    };
                    var committed = false;
                    try
                    {
                        using var connection = _openConnection();
                        using var transaction = connection.BeginTransaction();
                        if (!_accountManager.TryDebitLaborOn(debit, connection, transaction))
                        {
                            transaction.Rollback();
                            return FailedLabor(ButlerChargeOperationFailure.ConcurrentChange);
                        }
                        _repository.Save(proposed, connection, transaction);
                        transaction.Commit();
                        committed = true;

                        publication = _accountManager.ApplyCommittedLaborDebit(character, debit);
                        butler.Apply(proposed);
                        PublishLaborPowerUpdate(character, quote);
                        return new ButlerLaborPowerChargeResult(
                            true, ButlerChargeOperationFailure.None, ButlerChargeFailure.None, quote);
                    }
                    catch (Exception ex) when (!committed)
                    {
                        Logger.Error(ex, "Failed to charge farmhand labor power for character {0}", character.Id);
                        return FailedLabor(ButlerChargeOperationFailure.PersistenceFailed);
                    }
                }
            });
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        publication?.Publish();
        return result;
    }

    /// <summary>
    /// Persists UTC daily and world-local weekly quota rollover before a Butler state snapshot is sent.
    /// </summary>
    public ButlerQuotaRefreshResult RefreshQuotaPeriods(CharacterButler butler) =>
        RefreshQuotaPeriods(butler, null);

    /// <summary>Refreshes quota periods and publishes the changed counter groups to an online owner.</summary>
    public ButlerQuotaRefreshResult RefreshQuotaPeriods(Character character)
    {
        if (character == null)
            return FailedQuotaRefresh(ButlerChargeOperationFailure.InvalidContent);
        return RefreshQuotaPeriods(_butlerManager.GetOrCreate(character.Id), character);
    }

    private ButlerQuotaRefreshResult RefreshQuotaPeriods(CharacterButler butler, Character publishTo)
    {
        if (butler == null)
            return FailedQuotaRefresh(ButlerChargeOperationFailure.InvalidContent);

        PersistenceGate.EnterOperation();
        try
        {
            lock (butler.OperationSyncRoot)
            lock (butler.SyncRoot)
            {
                if (butler.IsDeleted)
                    return FailedQuotaRefresh(ButlerChargeOperationFailure.InvalidContent);
                if (publishTo != null &&
                    (publishTo.Id != butler.CharacterId || butler.HouseId == 0))
                    return new ButlerQuotaRefreshResult(true, ButlerChargeOperationFailure.None,
                        false, false, false);

                var nowUtc = NormalizeUtc(_utcNow());
                if (!TryResolveDailyLaborCounter(
                        butler, nowUtc, out var dailyChargedAmount, out var currentDailyPeriod) ||
                    !_contextResolver.TryResolve(butler, out var context) ||
                    !ValidContext(context) ||
                    !TryResolveWeeklyCounters(
                        butler, context.ContentConfig, nowUtc, out var weeklyCounters))
                    return FailedQuotaRefresh(ButlerChargeOperationFailure.InvalidContent);

                var dailyChanged = butler.LpChargedAmount != dailyChargedAmount ||
                                   butler.LpChargeResetTime != currentDailyPeriod;
                var originalWeeklyCounters = new ButlerProductionCostChargeCounters(
                    butler.PermanentDatas.GetValueOrDefault(
                        ButlerChargeRules.WeeklyFreeChargeCountPermanentDataKey),
                    butler.PermanentDatas.GetValueOrDefault(
                        ButlerChargeRules.WeeklyChargedAmountPermanentDataKey),
                    butler.PermanentDatas.GetValueOrDefault(
                        ButlerChargeRules.WeeklyResetAnchorPermanentDataKey));
                var weeklyChanged = weeklyCounters != originalWeeklyCounters;
                if (!dailyChanged && !weeklyChanged)
                    return new ButlerQuotaRefreshResult(true, ButlerChargeOperationFailure.None,
                        false, false, false);

                var proposed = butler.Snapshot() with
                {
                    LpChargedAmount = dailyChargedAmount,
                    LpChargeResetTime = currentDailyPeriod
                };
                var committed = false;
                try
                {
                    using var connection = _openConnection();
                    using var transaction = connection.BeginTransaction();
                    if (dailyChanged)
                        _repository.Save(proposed, connection, transaction);
                    if (weeklyChanged)
                        SaveCounters(butler.CharacterId, weeklyCounters, connection, transaction);
                    transaction.Commit();
                    committed = true;

                    if (dailyChanged)
                        butler.Apply(proposed);
                    if (weeklyChanged)
                        ApplyCounters(butler, weeklyCounters);
                    if (publishTo != null)
                        PublishQuotaRefresh(publishTo, butler, dailyChanged, weeklyChanged, weeklyCounters);
                    return new ButlerQuotaRefreshResult(true, ButlerChargeOperationFailure.None,
                        true, dailyChanged, weeklyChanged);
                }
                catch (Exception ex) when (!committed)
                {
                    Logger.Error(ex, "Failed to refresh farmhand quotas for character {0}",
                        butler.CharacterId);
                    return FailedQuotaRefresh(ButlerChargeOperationFailure.PersistenceFailed);
                }
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }
    }

    public ButlerProductionCostChargeResult ChargeFreeProductionCost(Character character) =>
        ChargeProductionCost(character);

    /// <summary>Applies a validated type-185 effect and consumes its exact source item atomically.</summary>
    public ButlerProductionCostChargeResult ChargePaidProductionCost(
        Character character, ulong sourceItemId, uint resolvedSpecialEffectValue, int consumeCount)
    {
        if (character == null || sourceItemId == 0 || resolvedSpecialEffectValue == 0 || consumeCount <= 0)
            return FailedProductionCost(ButlerChargeOperationFailure.InvalidContent);

        var butler = _butlerManager.GetOrCreate(character.Id);
        ItemConsumptionPublication publication = null;
        uint consumedTemplateId = 0;
        ButlerProductionCostChargeResult result;
        PersistenceGate.EnterOperation();
        try
        {
            lock (butler.OperationSyncRoot)
            lock (butler.SyncRoot)
            {
                if (!CanOperate(character, butler))
                    return FailedProductionCost(ButlerChargeOperationFailure.NotBound);
                if (!_contextResolver.TryResolve(character, butler, out var context) || !ValidContext(context))
                    return FailedProductionCost(ButlerChargeOperationFailure.InvalidContent);

                var nowUtc = NormalizeUtc(_utcNow());
                if (!TryResolveWeeklyCounters(
                        butler, context.ContentConfig, nowUtc, out var counters))
                    return FailedProductionCost(ButlerChargeOperationFailure.InvalidContent);
                var request = new ButlerProductionCostChargeRequest(
                    butler.RemainProductionCost,
                    context.MaximumProductionCost,
                    counters,
                    context.ContentConfig.ProductionCostWeeklyChargeAmountLimit);
                if (!ButlerChargeRules.TryQuotePaidProductionCostCharge(
                        request, resolvedSpecialEffectValue, out var quote, out var ruleFailure))
                    return new ButlerProductionCostChargeResult(
                        false, ButlerChargeOperationFailure.None, ruleFailure, default);
                if (quote.NewProductionCost > ushort.MaxValue)
                    return FailedProductionCost(ButlerChargeOperationFailure.InvalidContent);
                if (!character.Inventory.TryAcquireFarmhandMutation(out var inventoryLease))
                    return FailedProductionCost(ButlerChargeOperationFailure.Busy);

                using (inventoryLease)
                {
                    if (!character.Inventory.TryPlanExactBagConsumption(
                            sourceItemId, consumeCount, out var consumption))
                        return FailedProductionCost(ButlerChargeOperationFailure.InvalidSourceItem);

                    var snapshots = consumption.CapturePersistenceSnapshots(_itemManager);
                    consumedTemplateId = consumption.TemplateId;
                    var proposed = butler.Snapshot() with
                    {
                        RemainProductionCost = checked((ushort)quote.NewProductionCost)
                    };
                    var committed = false;
                    try
                    {
                        using var connection = _openConnection();
                        using var transaction = connection.BeginTransaction();
                        _repository.Save(proposed, connection, transaction);
                        SaveCounters(butler.CharacterId, quote.NewCounters, connection, transaction);
                        _itemManager.PersistSnapshots(connection, transaction, snapshots);
                        transaction.Commit();
                        committed = true;

                        publication = consumption.ApplyCommitted(ItemTaskType.SkillReagents);
                        butler.Apply(proposed);
                        ApplyCounters(butler, quote.NewCounters);
                        publication.PublishPackets();
                        PublishProductionCostUpdate(character, quote);
                        result = new ButlerProductionCostChargeResult(
                            true, ButlerChargeOperationFailure.None, ButlerChargeFailure.None, quote);
                    }
                    catch (Exception ex) when (!committed)
                    {
                        Logger.Error(ex,
                            "Failed to charge farmhand production cost from item {0} for character {1}",
                            sourceItemId, character.Id);
                        return FailedProductionCost(ButlerChargeOperationFailure.PersistenceFailed);
                    }
                }
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        publication?.PublishCallbacks();
        if (publication != null)
            character.ItemUseByTemplate(consumedTemplateId);
        return result;
    }

    /// <summary>Applies a validated type-186 effect and consumes its exact source item atomically.</summary>
    public ButlerExperienceGrantResult AddExperience(
        Character character, ulong sourceItemId, int resolvedSpecialEffectValue, int consumeCount)
    {
        if (character == null || sourceItemId == 0 || consumeCount <= 0)
            return FailedExperience(ButlerChargeOperationFailure.InvalidContent);

        var butler = _butlerManager.GetOrCreate(character.Id);
        ItemConsumptionPublication publication = null;
        uint consumedTemplateId = 0;
        ButlerExperienceGrantResult result;
        PersistenceGate.EnterOperation();
        try
        {
            lock (butler.OperationSyncRoot)
            lock (butler.SyncRoot)
            {
                if (!CanOperate(character, butler))
                    return FailedExperience(ButlerChargeOperationFailure.NotBound);

                var previousExperience = butler.PermanentDatas.GetValueOrDefault(
                    ButlerProgression.CumulativeExperiencePermanentDataKey);
                if (!ButlerProgression.TryAddExperience(
                        previousExperience, resolvedSpecialEffectValue, out var newExperience))
                    return FailedExperience(ButlerChargeOperationFailure.InvalidContent);
                if (!character.Inventory.TryAcquireFarmhandMutation(out var inventoryLease))
                    return FailedExperience(ButlerChargeOperationFailure.Busy);

                using (inventoryLease)
                {
                    if (!character.Inventory.TryPlanExactBagConsumption(
                            sourceItemId, consumeCount, out var consumption))
                        return FailedExperience(ButlerChargeOperationFailure.InvalidSourceItem);

                    var snapshots = consumption.CapturePersistenceSnapshots(_itemManager);
                    consumedTemplateId = consumption.TemplateId;
                    var committed = false;
                    try
                    {
                        using var connection = _openConnection();
                        using var transaction = connection.BeginTransaction();
                        _repository.SavePermanentData(
                            butler.CharacterId,
                            ButlerProgression.CumulativeExperiencePermanentDataKey,
                            newExperience,
                            connection,
                            transaction);
                        _itemManager.PersistSnapshots(connection, transaction, snapshots);
                        transaction.Commit();
                        committed = true;

                        publication = consumption.ApplyCommitted(ItemTaskType.SkillReagents);
                        butler.ApplyPermanentData(
                            ButlerProgression.CumulativeExperiencePermanentDataKey, newExperience);
                        publication.PublishPackets();
                        PublishExperienceUpdate(character, newExperience);
                        result = new ButlerExperienceGrantResult(
                            true,
                            ButlerChargeOperationFailure.None,
                            previousExperience,
                            newExperience);
                    }
                    catch (Exception ex) when (!committed)
                    {
                        Logger.Error(ex,
                            "Failed to add farmhand experience from item {0} for character {1}",
                            sourceItemId, character.Id);
                        return FailedExperience(ButlerChargeOperationFailure.PersistenceFailed);
                    }
                }
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        publication?.PublishCallbacks();
        if (publication != null)
            character.ItemUseByTemplate(consumedTemplateId);
        return result;
    }

    private ButlerProductionCostChargeResult ChargeProductionCost(Character character)
    {
        if (character == null)
            return FailedProductionCost(ButlerChargeOperationFailure.InvalidContent);

        var butler = _butlerManager.GetOrCreate(character.Id);
        PersistenceGate.EnterOperation();
        try
        {
            lock (butler.OperationSyncRoot)
            lock (butler.SyncRoot)
            {
                if (!CanOperate(character, butler))
                    return FailedProductionCost(ButlerChargeOperationFailure.NotBound);
                if (!_contextResolver.TryResolve(character, butler, out var context) || !ValidContext(context))
                    return FailedProductionCost(ButlerChargeOperationFailure.InvalidContent);

                var nowUtc = NormalizeUtc(_utcNow());
                if (!TryResolveWeeklyCounters(
                        butler, context.ContentConfig, nowUtc, out var counters))
                    return FailedProductionCost(ButlerChargeOperationFailure.InvalidContent);
                var request = new ButlerProductionCostChargeRequest(
                    butler.RemainProductionCost,
                    context.MaximumProductionCost,
                    counters,
                    context.ContentConfig.ProductionCostWeeklyChargeAmountLimit);
                var quoted = ButlerChargeRules.TryQuoteFreeProductionCostCharge(
                    request,
                    context.ContentConfig.ProductionCostWeeklyFreeChargeLimit,
                    context.ContentConfig.ProductionCostFreeChargeAmount,
                    out var quote,
                    out var ruleFailure);
                if (!quoted)
                    return new ButlerProductionCostChargeResult(
                        false, ButlerChargeOperationFailure.None, ruleFailure, default);
                if (quote.NewProductionCost > ushort.MaxValue)
                    return FailedProductionCost(ButlerChargeOperationFailure.InvalidContent);

                var proposed = butler.Snapshot() with
                {
                    RemainProductionCost = checked((ushort)quote.NewProductionCost)
                };
                var committed = false;
                try
                {
                    using var connection = _openConnection();
                    using var transaction = connection.BeginTransaction();
                    _repository.Save(proposed, connection, transaction);
                    SaveCounters(butler.CharacterId, quote.NewCounters, connection, transaction);
                    transaction.Commit();
                    committed = true;

                    butler.Apply(proposed);
                    ApplyCounters(butler, quote.NewCounters);
                    PublishProductionCostUpdate(character, quote);
                    return new ButlerProductionCostChargeResult(
                        true, ButlerChargeOperationFailure.None, ButlerChargeFailure.None, quote);
                }
                catch (Exception ex) when (!committed)
                {
                    Logger.Error(ex, "Failed to charge farmhand production cost for character {0}", character.Id);
                    return FailedProductionCost(ButlerChargeOperationFailure.PersistenceFailed);
                }
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }
    }

    private bool TryResolveWeeklyCounters(
        CharacterButler butler,
        ButlerChargeContentConfig config,
        DateTime nowUtc,
        out ButlerProductionCostChargeCounters counters)
    {
        var freeCount = butler.PermanentDatas.GetValueOrDefault(
            ButlerChargeRules.WeeklyFreeChargeCountPermanentDataKey);
        var chargedAmount = butler.PermanentDatas.GetValueOrDefault(
            ButlerChargeRules.WeeklyChargedAmountPermanentDataKey);
        var anchor = butler.PermanentDatas.GetValueOrDefault(
            ButlerChargeRules.WeeklyResetAnchorPermanentDataKey);
        if (anchor > long.MaxValue)
        {
            counters = default;
            return false;
        }

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.Local);
        var reset = anchor == 0;
        if (!reset)
        {
            try
            {
                var anchorLocal = TimeZoneInfo.ConvertTime(
                    DateTimeOffset.FromUnixTimeSeconds((long)anchor), TimeZoneInfo.Local).DateTime;
                reset = ButlerChargeRules.RequiresWeeklyReset(
                    anchorLocal, nowLocal, config.ProductionCostWeeklyFreeChargeResetDay);
            }
            catch (ArgumentOutOfRangeException)
            {
                counters = default;
                return false;
            }
        }

        counters = reset
            ? new ButlerProductionCostChargeCounters(0, 0, checked((ulong)Helpers.UnixTime(nowUtc)))
            : new ButlerProductionCostChargeCounters(freeCount, chargedAmount, anchor);
        return true;
    }

    internal static bool TryResolveDailyLaborCounter(CharacterButler butler, DateTime nowUtc,
        out ushort chargedAmount, out long currentPeriod)
    {
        var currentDay = ServerCalendar.AsUtc(nowUtc).Date;
        currentPeriod = Helpers.UnixTime(currentDay);
        chargedAmount = 0;
        if (currentPeriod <= 0)
            return false;

        if (butler.LpChargeResetTime == 0)
            return true;

        DateTime storedDay;
        try
        {
            storedDay = DateTimeOffset.FromUnixTimeSeconds(butler.LpChargeResetTime).UtcDateTime.Date;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        if (storedDay > currentDay)
            return false;
        if (storedDay == currentDay)
            chargedAmount = butler.LpChargedAmount;
        return true;
    }

    private void SaveCounters(uint characterId, ButlerProductionCostChargeCounters counters,
        MySqlConnection connection, MySqlTransaction transaction)
    {
        _repository.SavePermanentData(characterId,
            ButlerChargeRules.WeeklyFreeChargeCountPermanentDataKey,
            counters.WeeklyFreeChargeCount, connection, transaction);
        _repository.SavePermanentData(characterId,
            ButlerChargeRules.WeeklyChargedAmountPermanentDataKey,
            counters.WeeklyChargedAmount, connection, transaction);
        _repository.SavePermanentData(characterId,
            ButlerChargeRules.WeeklyResetAnchorPermanentDataKey,
            counters.WeeklyResetAnchor, connection, transaction);
    }

    private static void ApplyCounters(CharacterButler butler, ButlerProductionCostChargeCounters counters)
    {
        butler.ApplyPermanentData(ButlerChargeRules.WeeklyFreeChargeCountPermanentDataKey,
            counters.WeeklyFreeChargeCount);
        butler.ApplyPermanentData(ButlerChargeRules.WeeklyChargedAmountPermanentDataKey,
            counters.WeeklyChargedAmount);
        butler.ApplyPermanentData(ButlerChargeRules.WeeklyResetAnchorPermanentDataKey,
            counters.WeeklyResetAnchor);
    }

    private static void PublishProductionCostUpdate(
        Character character, ButlerProductionCostChargeQuote quote)
    {
        var permanentDatas = new Dictionary<sbyte, ulong>
        {
            [ButlerChargeRules.WeeklyFreeChargeCountPermanentDataKey] =
                quote.NewCounters.WeeklyFreeChargeCount,
            [ButlerChargeRules.WeeklyChargedAmountPermanentDataKey] =
                quote.NewCounters.WeeklyChargedAmount,
            [ButlerChargeRules.WeeklyResetAnchorPermanentDataKey] =
                quote.NewCounters.WeeklyResetAnchor
        };
        character.SendPacket(new SCButlerInfoUpdatedPacket(
            (ushort)ErrorMessageType.NoErrorMessage,
            ProductionCostAndPermanentDatasUpdatedFlags,
            false,
            false,
            Array.Empty<ButlerActabilityWire>(),
            permanentDatas,
            0,
            0,
            checked((ushort)quote.NewProductionCost),
            string.Empty,
            new Dictionary<uint, uint>()));
    }

    private static void PublishLaborPowerUpdate(
        Character character, ButlerLaborPowerChargeQuote quote)
    {
        character.SendPacket(new SCButlerInfoUpdatedPacket(
            (ushort)ErrorMessageType.NoErrorMessage,
            LaborPowerUpdatedFlags,
            false,
            false,
            Array.Empty<ButlerActabilityWire>(),
            new Dictionary<sbyte, ulong>(),
            quote.NewButlerLaborPower,
            quote.NewDailyChargedAmount,
            0,
            string.Empty,
            new Dictionary<uint, uint>()));
    }

    private static void PublishExperienceUpdate(Character character, ulong experience)
    {
        character.SendPacket(new SCButlerInfoUpdatedPacket(
            (ushort)ErrorMessageType.NoErrorMessage,
            PermanentDatasUpdatedFlags,
            false,
            false,
            Array.Empty<ButlerActabilityWire>(),
            new Dictionary<sbyte, ulong>
            {
                [ButlerProgression.CumulativeExperiencePermanentDataKey] = experience
            },
            0,
            0,
            0,
            string.Empty,
            new Dictionary<uint, uint>()));
    }

    private static void PublishQuotaRefresh(
        Character character,
        CharacterButler butler,
        bool dailyChanged,
        bool weeklyChanged,
        ButlerProductionCostChargeCounters weeklyCounters)
    {
        var flags = dailyChanged && weeklyChanged
            ? LaborPowerAndPermanentDatasUpdatedFlags
            : dailyChanged
                ? LaborPowerUpdatedFlags
                : PermanentDatasUpdatedFlags;
        var permanentDatas = weeklyChanged
            ? new Dictionary<sbyte, ulong>
            {
                [ButlerChargeRules.WeeklyFreeChargeCountPermanentDataKey] =
                    weeklyCounters.WeeklyFreeChargeCount,
                [ButlerChargeRules.WeeklyChargedAmountPermanentDataKey] =
                    weeklyCounters.WeeklyChargedAmount,
                [ButlerChargeRules.WeeklyResetAnchorPermanentDataKey] =
                    weeklyCounters.WeeklyResetAnchor
            }
            : new Dictionary<sbyte, ulong>();
        character.SendPacket(new SCButlerInfoUpdatedPacket(
            (ushort)ErrorMessageType.NoErrorMessage,
            flags,
            false,
            false,
            Array.Empty<ButlerActabilityWire>(),
            permanentDatas,
            butler.LaborPower,
            butler.LpChargedAmount,
            0,
            string.Empty,
            new Dictionary<uint, uint>()));
    }

    private static bool ValidContext(ButlerChargeContext context) =>
        context.MaximumLaborPower > 0 && context.LaborPowerChargeRate > 0 &&
        context.MaximumProductionCost is > 0 and <= ushort.MaxValue &&
        context.ContentConfig.ProductionCostWeeklyChargeAmountLimit > 0 &&
        context.ContentConfig.ProductionCostWeeklyFreeChargeLimit > 0 &&
        context.ContentConfig.ProductionCostFreeChargeAmount > 0 &&
        context.ContentConfig.LpDailyChargeAmountLimit > 0 &&
        context.ContentConfig.LpChargeMinimum > 0 &&
        Enum.IsDefined(context.ContentConfig.ProductionCostWeeklyFreeChargeResetDay);

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static bool CanOperate(Character character, CharacterButler butler) =>
        character.Id == butler.CharacterId && !butler.IsDeleted && butler.HouseId != 0;

    private static ButlerLaborPowerChargeResult FailedLabor(ButlerChargeOperationFailure failure) =>
        new(false, failure, ButlerChargeFailure.None, default);

    private static ButlerProductionCostChargeResult FailedProductionCost(ButlerChargeOperationFailure failure) =>
        new(false, failure, ButlerChargeFailure.None, default);

    private static ButlerExperienceGrantResult FailedExperience(ButlerChargeOperationFailure failure) =>
        new(false, failure, 0, 0);

    private static ButlerQuotaRefreshResult FailedQuotaRefresh(ButlerChargeOperationFailure failure) =>
        new(false, failure, false, false, false);
}
