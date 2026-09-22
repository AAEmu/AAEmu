using System.Collections.ObjectModel;
using System.Text;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Trading;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Specialty;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class SpecialtyManager(
    IItemManager itemManager,
    ILocalizationManager localizationManager,
    ISkillManager skillManager,
    IZoneManager zoneManager,
    IMailManager mailManager,
    SpecialtySaleCommitter saleCommitter,
    ISpecialtyMarketStore marketStore,
    ISpecialtyPurchaseStore purchaseStore,
    IWorldManager worldManager,
    ITaskManager taskManager,
    TimeProvider timeProvider,
    IOptions<AppConfiguration> options) : Singleton<SpecialtyManager>, ISpecialtyManager
{
    private const int RatioUnitsPerPercent = 100;
    private const int RatioUnitsPerWireUnit = 10;
    private const uint NeutralWireRatio = 1000;
    private const uint FirstLandFactionChatRegionId = 2;
    private const uint LastLandFactionChatRegionId = 4;
    private const int MaxCurrentRatios = 128;
    private const int MaxHistoryRecords = 256;
    private const int QuotesPerPage = 20;
    private const int EventIdsPerPage = 50;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly object _marketLock = new();
    private readonly object _eventLock = new();
    private readonly SpecialtyEventRuntimePolicy _specialtyEventRuntimePolicy = CaptureSpecialtyEventRuntimePolicy(options);
    private Dictionary<uint, Specialty> _specialties = [];
    private Dictionary<uint, SpecialtyBundleItem> _specialtyBundleItems = [];
    private Dictionary<uint, SpecialtyNpc> _specialtyNpcs = [];
    private Dictionary<uint, Dictionary<uint, SpecialtyBundleItem>> _specialtyBundleItemsMapped = [];
    private Dictionary<uint, List<FreshnessGroupItem>> _freshnessGroups = [];
    private SpecialtyContentSettings _specialtyContentSettings;
    private SkillTemplate _specialtySaleSkill;
    private Dictionary<uint, TradeGood> _tradeGoods = [];
    private Dictionary<uint, TradeGoodCategory> _tradeGoodCategories = [];
    private Dictionary<uint, List<TradeGood>> _tradeGoodsByCategory = [];
    private Dictionary<(uint CategoryId, uint ItemId), TradeGood> _tradeGoodsByCategoryAndItem = [];
    private Dictionary<uint, List<TradeGoodMaterial>> _tradeGoodMaterialsByTradeGoodId = [];
    private List<TradeGoodPriceIndex> _tradeGoodPriceIndices = [];
    private SkillTemplate _tradeGoodInteractionSkill;
    private SkillTemplate _tradeGoodSaleSkill;
    private SkillTemplate _tradeGoodPurchaseSkill;
    private int _tradeGoodSellLevelLimit;
    private int _tradeGoodMailInterest;
    private int _tradeGoodCoinPerGoldRatio;
    private int _tradeGoodBuyLevelLimit;
    private uint _goodsStockLimit;
    private uint _tradeGoodStockLimit;
    private SpecialtyEventCatalog _specialtyEventCatalog = SpecialtyEventCatalog.Empty;
    private Dictionary<(uint EventId, SpecialtyEventActivationSource Source), ActiveSpecialtyEventState> _activeSpecialtyEvents = [];
    private long _specialtyEventActivationToken;
    private bool _subscribedToZoneConflictStateChanges;
    private bool _initialized;
    private bool _reconcilingMarketConflict;
    private long _stockEventScheduleGeneration;
    private readonly Dictionary<uint, SpecialtyStockEventCheckTask> _scheduledStockEventChecks = [];
    private SpecialtyRatioRegenTask _timedRatioRecoveryTask;

    // Specialty item -> destination zone group -> hundredths of a percentage point.
    private SpecialtyMarketState _market = new();
    private Dictionary<uint, Dictionary<uint, int>> _priceRatios => _market.PriceRatios;
    // Specialty item -> destination zone group -> deliveries toward the next demand adjustment.
    private Dictionary<uint, Dictionary<uint, int>> _demandRemainders => _market.DemandRemainders;
    // Destination zone group and material tag -> FIFO runs of delivered packs not yet converted into cargo.
    private Dictionary<(uint ZoneGroupId, uint TagId), List<SpecialtyMaterialContribution>> _tradeGoodMaterialContributions =>
        _market.MaterialContributions;
    // Destination zone group and tradegood row -> produced cargo units available for purchase.
    private Dictionary<(uint ZoneGroupId, uint TradeGoodId), uint> _tradeGoodCargoStock => _market.CargoStock;
    // Character id -> source/destination routes watched by the specialty information UI.
    private Dictionary<uint, HashSet<(ushort FromZoneGroupId, ushort ToZoneGroupId)>> _subscriptions = [];
    private Dictionary<(uint ItemId, uint ZoneGroupId), List<SpecialtyMarketRecord>> _records => _market.Records;

    public void Load()
    {
        var reinitialize = _initialized;
        ClearScheduledStockEventChecks();
        ClearActiveSpecialtyEvents();
        ClearTimedRatioRecoveryTask();
        lock (_marketLock)
        {
            _specialties = [];
            _specialtyBundleItems = [];
            _specialtyNpcs = [];
            _specialtyBundleItemsMapped = [];
            _freshnessGroups = [];
            _specialtyContentSettings = null;
            _specialtySaleSkill = null;
            _tradeGoods = [];
            _tradeGoodCategories = [];
            _tradeGoodsByCategory = [];
            _tradeGoodsByCategoryAndItem = [];
            _tradeGoodMaterialsByTradeGoodId = [];
            _tradeGoodPriceIndices = [];
            _tradeGoodInteractionSkill = null;
            _tradeGoodSaleSkill = null;
            _tradeGoodPurchaseSkill = null;
            _tradeGoodSellLevelLimit = 0;
            _tradeGoodMailInterest = 0;
            _tradeGoodCoinPerGoldRatio = 0;
            _tradeGoodBuyLevelLimit = 0;
            _goodsStockLimit = 0;
            _tradeGoodStockLimit = 0;
            _specialtyEventCatalog = SpecialtyEventCatalog.Empty;
            _market = new();
            _subscriptions = [];
        }

        Logger.Info("SpecialtyManager is loading...");
        using var connection = SQLite.CreateConnection();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, row_zone_group_id, col_zone_group_id FROM specialties";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var specialty = new Specialty
                {
                    Id = reader.GetUInt32("id"),
                    RowZoneGroupId = reader.GetUInt32("row_zone_group_id"),
                    ColZoneGroupId = reader.GetUInt32("col_zone_group_id")
                };
                _specialties.Add(specialty.Id, specialty);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, item_id, specialty_bundle_id, profit, ratio FROM specialty_bundle_items";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var bundleItem = new SpecialtyBundleItem
                {
                    Id = reader.GetUInt32("id"),
                    ItemId = reader.GetUInt32("item_id"),
                    SpecialtyBundleId = reader.GetUInt32("specialty_bundle_id"),
                    Profit = reader.GetUInt32("profit"),
                    Ratio = reader.GetInt32("ratio")
                };
                _specialtyBundleItems.Add(bundleItem.Id, bundleItem);
                if (!_specialtyBundleItemsMapped.TryGetValue(bundleItem.ItemId, out var byBundle))
                {
                    byBundle = [];
                    _specialtyBundleItemsMapped.Add(bundleItem.ItemId, byBundle);
                }
                byBundle.Add(bundleItem.SpecialtyBundleId, bundleItem);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name, npc_id, specialty_bundle_id, zone_group_id FROM specialty_npcs";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var specialtyNpc = new SpecialtyNpc
                {
                    Id = reader.GetUInt32("id"),
                    Name = reader.GetString("name"),
                    NpcId = reader.GetUInt32("npc_id"),
                    SpecialtyBundleId = reader.GetUInt32("specialty_bundle_id"),
                    ZoneGroupId = reader.GetUInt32("zone_group_id")
                };
                _specialtyNpcs.Add(specialtyNpc.NpcId, specialtyNpc);
            }
        }

        LoadFreshnessData(connection);
        LoadSpecialtySaleData(connection);
        LoadTradeGoodData(connection);
        LoadSpecialtyEventData(connection);

        foreach (var bundleItem in _specialtyBundleItems.Values)
        {
            bundleItem.Item = itemManager.GetTemplate(bundleItem.ItemId);
            if (bundleItem.Item == null)
                throw new InvalidDataException(
                    $"specialty_bundle_items row {bundleItem.Id} references missing item template {bundleItem.ItemId}.");
        }

        RestoreMarketState();

        if (reinitialize)
            Initialize();

        Logger.Info(
            "Loaded {0} routes, {1} bundle items, {2} NPCs, {3} freshness groups, {4} cargo categories, {5} cargo goods, {6} price indices and {7} specialty event descriptors",
            _specialties.Count,
            _specialtyBundleItems.Count,
            _specialtyNpcs.Count,
            _freshnessGroups.Count,
            _tradeGoodCategories.Count,
            _tradeGoods.Count,
            _tradeGoodPriceIndices.Count,
            _specialtyEventCatalog.Events.Count);
    }

    public SpecialtyEventCatalog SpecialtyEventCatalog => _specialtyEventCatalog;

    public bool SpecialtyEventsEnabled => _specialtyEventRuntimePolicy.Enabled;
    public uint DefaultManualSpecialtyEventDurationSeconds => _specialtyEventRuntimePolicy.DefaultManualDurationSeconds;

    public IReadOnlyList<ActiveSpecialtyEvent> GetActiveSpecialtyEvents()
    {
        lock (_eventLock)
        {
            var now = timeProvider.GetUtcNow();
            return _activeSpecialtyEvents.Values
                .Select(x => x.Activation)
                .Where(x => x.ExpiresAt > now)
                .GroupBy(x => x.EventId)
                .Select(x => x.OrderByDescending(y => y.ExpiresAt).First())
                .OrderBy(x => x.EventId)
                .ToArray();
        }
    }

    public bool TryActivateSpecialtyEvent(
        uint eventId,
        TimeSpan duration,
        uint activatedByCharacterId,
        out ActiveSpecialtyEvent activation,
        out string error)
    {
        activation = null;
        error = null;
        if (!_specialtyEventCatalog.Events.TryGetValue(eventId, out var descriptor))
        {
            error = $"Unknown specialty event {eventId}.";
            return false;
        }
        if (duration <= TimeSpan.Zero)
        {
            error = "Specialty event duration must be positive.";
            return false;
        }

        var broadcastStart = false;
        lock (_eventLock)
        {
            if (!SpecialtyEventsEnabled)
            {
                error = "Specialty events are disabled by configuration.";
                return false;
            }
            var startedAt = timeProvider.GetUtcNow();
            DateTimeOffset expiresAt;
            try
            {
                expiresAt = startedAt.Add(duration);
            }
            catch (ArgumentOutOfRangeException)
            {
                error = "Specialty event duration is too large.";
                return false;
            }
            var token = ++_specialtyEventActivationToken;
            var expiryTask = new SpecialtyEventExpiryTask(
                this,
                eventId,
                token,
                SpecialtyEventActivationSource.Manual);
            if (!taskManager.Schedule(expiryTask, duration))
            {
                error = $"Failed to schedule expiry for specialty event {eventId}.";
                return false;
            }

            activation = new ActiveSpecialtyEvent(eventId, startedAt, expiresAt, activatedByCharacterId);
            var key = (eventId, SpecialtyEventActivationSource.Manual);
            var triggerWasActive = IsSpecialtyEventTriggerActiveNoLock(descriptor.Trigger.Id);
            _activeSpecialtyEvents.TryGetValue(key, out var previous);
            _activeSpecialtyEvents[key] = new ActiveSpecialtyEventState(activation, token, expiryTask);
            if (previous != null)
                taskManager.Cancel(previous.ExpiryTask);
            broadcastStart = !triggerWasActive;
        }
        if (broadcastStart)
            BroadcastSpecialtyEventMessage(descriptor, descriptor.Trigger.StartMessage);
        return true;
    }

    public bool TryDeactivateSpecialtyEvent(uint eventId, out ActiveSpecialtyEvent activation, out string error)
    {
        activation = null;
        error = null;
        ActiveSpecialtyEventState state;
        SpecialtyEventDescriptor descriptor = null;
        var broadcastEnd = false;
        lock (_eventLock)
        {
            var key = (eventId, SpecialtyEventActivationSource.Manual);
            if (!_activeSpecialtyEvents.Remove(key, out state))
            {
                error = $"Specialty event {eventId} is not manually active.";
                return false;
            }
            activation = state.Activation;
            taskManager.Cancel(state.ExpiryTask);
            if (SpecialtyEventsEnabled &&
                _specialtyEventCatalog.Events.TryGetValue(eventId, out descriptor) &&
                !IsSpecialtyEventTriggerActiveNoLock(descriptor.Trigger.Id))
                broadcastEnd = true;
        }
        if (broadcastEnd)
            BroadcastSpecialtyEventMessage(descriptor, descriptor.Trigger.EndMessage);
        return true;
    }

    internal void ExpireSpecialtyEvent(
        uint eventId,
        long activationToken,
        SpecialtyEventActivationSource source)
    {
        if (source == SpecialtyEventActivationSource.StockCount)
        {
            ExpirePersistedStockEvent(eventId, activationToken);
            return;
        }

        SpecialtyEventDescriptor descriptor = null;
        var broadcastEnd = false;
        lock (_eventLock)
        {
            var key = (eventId, source);
            if (!_activeSpecialtyEvents.TryGetValue(key, out var state) ||
                state.ActivationToken != activationToken ||
                state.Activation.ExpiresAt > timeProvider.GetUtcNow())
                return;
            _activeSpecialtyEvents.Remove(key);
            _specialtyEventCatalog.Events.TryGetValue(eventId, out descriptor);
            if (descriptor != null &&
                SpecialtyEventsEnabled &&
                !IsSpecialtyEventTriggerActiveNoLock(descriptor.Trigger.Id))
                broadcastEnd = true;
        }
        if (broadcastEnd)
            BroadcastSpecialtyEventMessage(descriptor, descriptor.Trigger.EndMessage);
    }

    private void ExpirePersistedStockEvent(uint eventId, long activationToken)
    {
        SpecialtyEventDescriptor descriptor = null;
        var broadcastEnd = false;
        lock (_marketLock)
        lock (_eventLock)
        {
            var key = (eventId, SpecialtyEventActivationSource.StockCount);
            if (!_activeSpecialtyEvents.TryGetValue(key, out var state) ||
                state.ActivationToken != activationToken ||
                state.Activation.ExpiresAt > timeProvider.GetUtcNow())
                return;

            var write = PrepareMarketWrite(() => _market.StockEventActivations.Remove(eventId));
            CommitMarketWrite(write);
            _activeSpecialtyEvents.Remove(key);
            _specialtyEventCatalog.Events.TryGetValue(eventId, out descriptor);
            if (descriptor != null &&
                SpecialtyEventsEnabled &&
                !IsSpecialtyEventTriggerActiveNoLock(descriptor.Trigger.Id))
                broadcastEnd = true;
        }
        if (broadcastEnd)
            BroadcastSpecialtyEventMessage(descriptor, descriptor.Trigger.EndMessage);
    }

    private void OnZoneConflictStateChanged(
        ushort zoneGroupId,
        ZoneConflictType previousState,
        ZoneConflictType currentState)
    {
        Logger.Debug(
            "Reconciling specialty events for ZoneGroup {0} state {1} -> {2}",
            zoneGroupId,
            previousState,
            currentState);
        ReconcileZoneConflictEvents(zoneGroupId, currentState);
    }

    internal void ReconcileZoneConflictEvents(ushort zoneGroupId, ZoneConflictType currentState)
    {
        if (!SpecialtyEventsEnabled)
            return;

        var triggerGroups = _specialtyEventCatalog.Events.Values
            .Where(x =>
                x.Trigger.Type == SpecialtyEventTriggerType.ZoneConflictState &&
                x.Trigger.ZoneGroupId == zoneGroupId)
            .GroupBy(x => x.Trigger.Id)
            .ToArray();
        foreach (var triggerGroup in triggerGroups)
            ReconcileZoneConflictTrigger(triggerGroup.ToArray(), currentState);
    }

    internal void CheckStockEventTrigger(uint triggerId, int roll) =>
        CheckStockEventTrigger(triggerId, roll, null);

    private void CheckStockEventTrigger(uint triggerId, int roll, long? scheduleGeneration)
    {
        if (roll is < 0 or >= 1000)
            throw new ArgumentOutOfRangeException(nameof(roll));
        if (!SpecialtyEventsEnabled)
            return;

        var descriptors = _specialtyEventCatalog.Events.Values
            .Where(x => x.Trigger.Id == triggerId && x.Trigger.Type == SpecialtyEventTriggerType.StockCount)
            .OrderBy(x => x.Id)
            .ToArray();
        if (descriptors.Length == 0)
            return;

        // Expiry tasks are one-shot. Retry their durable cleanup on subsequent stock checks,
        // even when today's stock/roll would not activate the trigger.
        (uint EventId, long Token)[] expired;
        lock (_eventLock)
        {
            var now = timeProvider.GetUtcNow();
            expired = descriptors
                .Select(descriptor => (descriptor.Id, State: _activeSpecialtyEvents.GetValueOrDefault(
                    (descriptor.Id, SpecialtyEventActivationSource.StockCount))))
                .Where(entry => entry.State != null && entry.State.Activation.ExpiresAt <= now)
                .Select(entry => (entry.Id, entry.State.ActivationToken))
                .ToArray();
        }
        foreach (var (eventId, token) in expired)
            ExpirePersistedStockEvent(eventId, token);

        var trigger = descriptors[0].Trigger;
        if (!TryGetStockEventCargoStock(trigger, out var stock))
            return;

        if (stock < checked((uint)trigger.Value1) || stock > checked((uint)trigger.Value2) ||
            roll >= trigger.EventRate)
            return;

        ActivateStockEventTrigger(descriptors, scheduleGeneration);
    }

    internal void RunStockEventCheck(uint triggerId, int roll) =>
        RunStockEventCheck(triggerId, roll, Interlocked.Read(ref _stockEventScheduleGeneration));

    internal void RunStockEventCheck(uint triggerId, int roll, long scheduleGeneration)
    {
        if (scheduleGeneration != Interlocked.Read(ref _stockEventScheduleGeneration))
            return;
        if (!_specialtyEventCatalog.Triggers.TryGetValue(triggerId, out var trigger) ||
            trigger.Type != SpecialtyEventTriggerType.StockCount)
            return;

        lock (_marketLock)
        {
            if (scheduleGeneration != Interlocked.Read(ref _stockEventScheduleGeneration))
                return;
            var nextCheck = timeProvider.GetUtcNow().AddSeconds(trigger.CheckTime).ToUnixTimeSeconds();
            var write = PrepareMarketWrite(() => _market.StockEventNextChecks[triggerId] = nextCheck);
            CommitMarketWrite(write);
        }
        CheckStockEventTrigger(triggerId, roll, scheduleGeneration);
    }

    private void ActivateStockEventTrigger(
        SpecialtyEventDescriptor[] descriptors,
        long? scheduleGeneration)
    {
        var trigger = descriptors[0].Trigger;
        var duration = TimeSpan.FromSeconds(trigger.EventTime);
        var broadcastStart = false;

        lock (_marketLock)
        lock (_eventLock)
        {
            if (scheduleGeneration.HasValue &&
                scheduleGeneration.Value != Interlocked.Read(ref _stockEventScheduleGeneration))
                return;
            var missingDescriptors = descriptors
                .Where(x => !_activeSpecialtyEvents.ContainsKey((x.Id, SpecialtyEventActivationSource.StockCount)))
                .ToArray();
            if (missingDescriptors.Length == 0)
                return;

            var triggerWasActive = IsSpecialtyEventTriggerActiveNoLock(trigger.Id);
            var startedAt = timeProvider.GetUtcNow();
            var pending = new List<((uint EventId, SpecialtyEventActivationSource Source) Key, ActiveSpecialtyEventState State)>();
            foreach (var descriptor in missingDescriptors)
            {
                var token = ++_specialtyEventActivationToken;
                var expiryTask = new SpecialtyEventExpiryTask(
                    this,
                    descriptor.Id,
                    token,
                    SpecialtyEventActivationSource.StockCount);
                if (!taskManager.Schedule(expiryTask, duration))
                {
                    foreach (var entry in pending)
                        taskManager.Cancel(entry.State.ExpiryTask);
                    Logger.Error(
                        "Failed to schedule automatic expiry for specialty event {0} from stock trigger {1}",
                        descriptor.Id,
                        trigger.Id);
                    return;
                }

                var activation = new ActiveSpecialtyEvent(
                    descriptor.Id,
                    startedAt,
                    startedAt.Add(duration),
                    0);
                pending.Add((
                    (descriptor.Id, SpecialtyEventActivationSource.StockCount),
                    new ActiveSpecialtyEventState(activation, token, expiryTask)));
            }

            try
            {
                var persisted = new SpecialtyStockEventActivation(
                    startedAt.ToUnixTimeSeconds(),
                    startedAt.Add(duration).ToUnixTimeSeconds());
                var write = PrepareMarketWrite(() =>
                {
                    foreach (var descriptor in missingDescriptors)
                        _market.StockEventActivations[descriptor.Id] = persisted;
                });
                CommitMarketWrite(write);
            }
            catch
            {
                foreach (var entry in pending)
                    taskManager.Cancel(entry.State.ExpiryTask);
                throw;
            }

            foreach (var entry in pending)
                _activeSpecialtyEvents[entry.Key] = entry.State;
            broadcastStart = !triggerWasActive && IsSpecialtyEventTriggerActiveNoLock(trigger.Id);
        }

        if (broadcastStart)
            BroadcastSpecialtyEventMessage(descriptors[0], trigger.StartMessage);
    }

    private void ReconcileZoneConflictTrigger(
        SpecialtyEventDescriptor[] descriptors,
        ZoneConflictType currentState)
    {
        var trigger = descriptors[0].Trigger;
        var shouldActivate =
            trigger.SubjectType == SpecialtyEventTriggerSubjectType.EnumHonorPointWarState &&
            trigger.SubjectId == (uint)currentState;
        var broadcastStart = false;
        var broadcastEnd = false;

        lock (_eventLock)
        {
            var triggerWasActive = IsSpecialtyEventTriggerActiveNoLock(trigger.Id);
            if (shouldActivate)
            {
                var duration = TimeSpan.FromSeconds(trigger.EventTime);
                foreach (var descriptor in descriptors)
                {
                    var key = (descriptor.Id, SpecialtyEventActivationSource.ZoneConflict);
                    if (_activeSpecialtyEvents.ContainsKey(key))
                        continue;

                    var startedAt = timeProvider.GetUtcNow();
                    var token = ++_specialtyEventActivationToken;
                    var expiryTask = new SpecialtyEventExpiryTask(
                        this,
                        descriptor.Id,
                        token,
                        SpecialtyEventActivationSource.ZoneConflict);
                    if (!taskManager.Schedule(expiryTask, duration))
                    {
                        Logger.Error(
                            "Failed to schedule automatic expiry for specialty event {0} from zone conflict trigger {1}",
                            descriptor.Id,
                            trigger.Id);
                        continue;
                    }

                    var activation = new ActiveSpecialtyEvent(
                        descriptor.Id,
                        startedAt,
                        startedAt.Add(duration),
                        0);
                    _activeSpecialtyEvents[key] = new ActiveSpecialtyEventState(activation, token, expiryTask);
                }
            }
            else
            {
                foreach (var descriptor in descriptors)
                {
                    var key = (descriptor.Id, SpecialtyEventActivationSource.ZoneConflict);
                    if (!_activeSpecialtyEvents.Remove(key, out var state))
                        continue;
                    taskManager.Cancel(state.ExpiryTask);
                }
            }

            var triggerIsActive = IsSpecialtyEventTriggerActiveNoLock(trigger.Id);
            broadcastStart = !triggerWasActive && triggerIsActive;
            broadcastEnd = triggerWasActive && !triggerIsActive;
        }

        if (broadcastStart)
            BroadcastSpecialtyEventMessage(descriptors[0], trigger.StartMessage);
        if (broadcastEnd)
            BroadcastSpecialtyEventMessage(descriptors[0], trigger.EndMessage);
    }

    private bool IsSpecialtyEventTriggerActiveNoLock(uint triggerId) =>
        _activeSpecialtyEvents.Keys.Any(x =>
            _specialtyEventCatalog.Events.TryGetValue(x.EventId, out var descriptor) &&
            descriptor.Trigger.Id == triggerId);

    internal IReadOnlyList<uint> GetActiveSpecialtyEventIds(uint zoneGroupId, IReadOnlyCollection<uint> itemIds)
        => GetActiveSpecialtyEventIds(CaptureSpecialtyEventSnapshot(), zoneGroupId, itemIds);

    private static uint[] GetActiveSpecialtyEventIds(
        SpecialtyEventSnapshot snapshot,
        uint zoneGroupId,
        IReadOnlyCollection<uint> itemIds)
    {
        var itemIdSet = itemIds.ToHashSet();
        return snapshot.Descriptors
            .Where(descriptor =>
                descriptor.Trigger.ZoneGroupId == zoneGroupId &&
                descriptor.TargetItemIds.Any(itemIdSet.Contains))
            .Select(x => x.Id)
            .ToArray();
    }

    private float GetActiveSpecialtyEventMultiplier(
        uint zoneGroupId,
        uint itemId,
        SpecialtyEventType eventType) =>
        GetActiveSpecialtyEventMultiplier(CaptureSpecialtyEventSnapshot(), zoneGroupId, itemId, eventType);

    private static float GetActiveSpecialtyEventMultiplier(
        SpecialtyEventSnapshot snapshot,
        uint zoneGroupId,
        uint itemId,
        SpecialtyEventType eventType)
    {
        var multiplier = 1f;
        foreach (var descriptor in snapshot.Descriptors)
        {
            if (descriptor.Type != eventType ||
                descriptor.Trigger.ZoneGroupId != zoneGroupId ||
                !descriptor.TargetItemIds.Contains(itemId))
                continue;
            multiplier *= descriptor.Value / 1000f;
        }

        if (!float.IsFinite(multiplier) || multiplier <= 0)
            throw new OverflowException(
                $"Active specialty event multiplier is invalid for item {itemId} in zone {zoneGroupId}.");
        return multiplier;
    }

    private SpecialtyEventSnapshot CaptureSpecialtyEventSnapshot()
    {
        if (!SpecialtyEventsEnabled)
            return SpecialtyEventSnapshot.Empty;

        lock (_eventLock)
        {
            var now = timeProvider.GetUtcNow();
            var descriptors = _activeSpecialtyEvents.Values
                .Select(x => x.Activation)
                .Where(x => x.ExpiresAt > now)
                .Select(x => x.EventId)
                .Distinct()
                .Select(x => _specialtyEventCatalog.Events.GetValueOrDefault(x))
                .Where(x => x != null)
                .OrderBy(x => x.Id)
                .ToArray();
            return new SpecialtyEventSnapshot(descriptors);
        }
    }

    private void ClearActiveSpecialtyEvents()
    {
        SpecialtyEventExpiryTask[] expiryTasks;
        lock (_eventLock)
        {
            expiryTasks = _activeSpecialtyEvents.Values.Select(x => x.ExpiryTask).ToArray();
            _activeSpecialtyEvents = [];
        }
        foreach (var expiryTask in expiryTasks)
            taskManager.Cancel(expiryTask);
    }

    private void ClearActiveStockEvents()
    {
        SpecialtyEventExpiryTask[] expiryTasks;
        lock (_eventLock)
        {
            var keys = _activeSpecialtyEvents.Keys
                .Where(x => x.Source == SpecialtyEventActivationSource.StockCount)
                .ToArray();
            expiryTasks = keys.Select(x => _activeSpecialtyEvents[x].ExpiryTask).ToArray();
            foreach (var key in keys)
                _activeSpecialtyEvents.Remove(key);
        }
        foreach (var expiryTask in expiryTasks)
            taskManager.Cancel(expiryTask);
    }

    private void ClearScheduledStockEventChecks()
    {
        SpecialtyStockEventCheckTask[] tasks;
        lock (_marketLock)
        lock (_eventLock)
        {
            Interlocked.Increment(ref _stockEventScheduleGeneration);
            tasks = _scheduledStockEventChecks.Values.ToArray();
            _scheduledStockEventChecks.Clear();
        }
        foreach (var task in tasks)
            taskManager.Cancel(task);
    }

    private void RestoreStockEventActivations()
    {
        var now = timeProvider.GetUtcNow();
        lock (_marketLock)
        lock (_eventLock)
        {
            foreach (var key in _activeSpecialtyEvents.Keys
                         .Where(x => x.Source == SpecialtyEventActivationSource.StockCount)
                         .ToArray())
            {
                taskManager.Cancel(_activeSpecialtyEvents[key].ExpiryTask);
                _activeSpecialtyEvents.Remove(key);
            }

            var staleEventIds = new List<uint>();
            var pending = new List<((uint EventId, SpecialtyEventActivationSource Source) Key, ActiveSpecialtyEventState State)>();
            foreach (var (eventId, persisted) in _market.StockEventActivations.OrderBy(x => x.Key))
            {
                if (!_specialtyEventCatalog.Events.TryGetValue(eventId, out var descriptor) ||
                    descriptor.Trigger.Type != SpecialtyEventTriggerType.StockCount ||
                    !TryReadPersistedActivation(persisted, out var startedAt, out var expiresAt) ||
                    expiresAt <= now)
                {
                    staleEventIds.Add(eventId);
                    continue;
                }

                var token = ++_specialtyEventActivationToken;
                var expiryTask = new SpecialtyEventExpiryTask(
                    this,
                    eventId,
                    token,
                    SpecialtyEventActivationSource.StockCount);
                if (!taskManager.Schedule(expiryTask, expiresAt - now))
                {
                    foreach (var entry in pending)
                        taskManager.Cancel(entry.State.ExpiryTask);
                    throw new InvalidOperationException($"Failed to restore expiry for specialty stock event {eventId}.");
                }
                pending.Add((
                    (eventId, SpecialtyEventActivationSource.StockCount),
                    new ActiveSpecialtyEventState(
                        new ActiveSpecialtyEvent(eventId, startedAt, expiresAt, 0),
                        token,
                        expiryTask)));
            }

            if (staleEventIds.Count > 0)
            {
                var write = PrepareMarketWrite(() =>
                {
                    foreach (var eventId in staleEventIds)
                        _market.StockEventActivations.Remove(eventId);
                });
                try
                {
                    CommitMarketWrite(write);
                }
                catch
                {
                    foreach (var entry in pending)
                        taskManager.Cancel(entry.State.ExpiryTask);
                    throw;
                }
            }

            foreach (var entry in pending)
                _activeSpecialtyEvents[entry.Key] = entry.State;
        }
    }

    private static bool TryReadPersistedActivation(
        SpecialtyStockEventActivation persisted,
        out DateTimeOffset startedAt,
        out DateTimeOffset expiresAt)
    {
        try
        {
            startedAt = DateTimeOffset.FromUnixTimeSeconds(persisted.StartedAt);
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(persisted.ExpiresAt);
            return expiresAt > startedAt;
        }
        catch (ArgumentOutOfRangeException)
        {
            startedAt = default;
            expiresAt = default;
            return false;
        }
    }

    private void BroadcastSpecialtyEventMessage(SpecialtyEventDescriptor descriptor, string message)
    {
        if (descriptor.Trigger.MessageScope == null || string.IsNullOrEmpty(message))
            return;

        foreach (var player in worldManager.GetAllCharacters() ?? [])
        {
            if (!player.IsOnline)
                continue;
            if (descriptor.Trigger.MessageScope == SpecialtyEventMessageScope.Zone &&
                zoneManager.GetZoneByKey(player.Transform.ZoneId)?.GroupId != descriptor.Trigger.ZoneGroupId)
                continue;
            try
            {
                player.SendPacket(new SCSpecialtyEventMsgPacket(message));
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, "Failed to send specialty event {0} message to character {1}", descriptor.Id, player.Id);
            }
        }
    }

    private static SpecialtyEventRuntimePolicy CaptureSpecialtyEventRuntimePolicy(IOptions<AppConfiguration> options)
    {
        var config = options.Value.Specialty;
        return new SpecialtyEventRuntimePolicy(config.EnableEvents, config.ManualEventDurationSeconds);
    }

    internal void LoadSpecialtyEventData(SqliteConnection connection)
    {
        var questContextGroupIds = new HashSet<uint>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM quest_context_groups";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
                questContextGroupIds.Add(ReadId(reader, "quest_context_groups", "id"));
        }

        var triggers = new Dictionary<uint, SpecialtyEventTriggerDescriptor>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, zone_group_id, trigger_type, trigger_value_1, trigger_value_2, check_time, " +
                "event_rate, event_time, msg_type, msg_start, msg_end, trigger_subject_id, trigger_subject_type " +
                "FROM specialty_event_triggers ORDER BY id";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var id = ReadId(reader, "specialty_event_triggers", "id");
                var zoneGroupId = ReadId(reader, "specialty_event_triggers", "zone_group_id", id);
                if (zoneManager.GetZoneGroupById(zoneGroupId) == null)
                    throw new InvalidDataException(
                        $"specialty_event_triggers row {id} references missing zone group {zoneGroupId}.");

                var typeValue = ReadInt32(reader, "specialty_event_triggers", "trigger_type", id);
                if (!Enum.IsDefined(typeof(SpecialtyEventTriggerType), typeValue))
                    throw new InvalidDataException(
                        $"specialty_event_triggers row {id} has unknown trigger_type {typeValue}.");
                var triggerType = (SpecialtyEventTriggerType)typeValue;
                var subjectType = ParseTriggerSubjectType(reader.GetString("trigger_subject_type"), id);
                var subjectId = ReadId(
                    reader,
                    "specialty_event_triggers",
                    "trigger_subject_id",
                    id,
                    subjectType == SpecialtyEventTriggerSubjectType.EnumHonorPointWarState);
                var subjectItemIds = ResolveEventItemIds(subjectType, subjectId, questContextGroupIds, id);
                var value1 = ReadInt32(reader, "specialty_event_triggers", "trigger_value_1", id);
                var value2 = ReadInt32(reader, "specialty_event_triggers", "trigger_value_2", id);
                var checkTime = ReadInt32(reader, "specialty_event_triggers", "check_time", id);
                var eventRate = ReadInt32(reader, "specialty_event_triggers", "event_rate", id);
                var eventTime = ReadInt32(reader, "specialty_event_triggers", "event_time", id);
                if (triggerType == SpecialtyEventTriggerType.ZoneConflictState)
                {
                    if (subjectType != SpecialtyEventTriggerSubjectType.EnumHonorPointWarState)
                        throw new InvalidDataException(
                            $"specialty_event_triggers row {id} has a zone-conflict trigger with subject type {subjectType}.");
                    if (eventTime <= 0)
                        throw new InvalidDataException(
                            $"specialty_event_triggers row {id} has non-positive event_time {eventTime}.");
                }
                else if (triggerType == SpecialtyEventTriggerType.StockCount)
                {
                    if (subjectType != SpecialtyEventTriggerSubjectType.Item)
                        throw new InvalidDataException(
                            $"specialty_event_triggers row {id} has a stock-count trigger with subject type {subjectType}.");
                    if (value1 < 0 || value2 <= 0 || value1 > value2)
                        throw new InvalidDataException(
                            $"specialty_event_triggers row {id} has invalid stock bounds {value1}..{value2}.");
                    if (checkTime <= 0 || eventTime <= 0)
                        throw new InvalidDataException(
                            $"specialty_event_triggers row {id} has non-positive timing values {checkTime}/{eventTime}.");
                    if (eventRate is < 0 or > 1000)
                        throw new InvalidDataException(
                            $"specialty_event_triggers row {id} has event_rate {eventRate} outside 0..1000.");
                    if (_tradeGoodStockLimit > 0 && value2 > _tradeGoodStockLimit)
                        throw new InvalidDataException(
                            $"specialty_event_triggers row {id} upper stock bound {value2} exceeds tradegoods_stock_limit {_tradeGoodStockLimit}.");
                }

                SpecialtyEventMessageScope? messageScope = null;
                if (!reader.IsDBNull("msg_type"))
                {
                    var scopeValue = ReadInt32(reader, "specialty_event_triggers", "msg_type", id);
                    if (!Enum.IsDefined(typeof(SpecialtyEventMessageScope), scopeValue))
                        throw new InvalidDataException(
                            $"specialty_event_triggers row {id} has unknown msg_type {scopeValue}.");
                    messageScope = (SpecialtyEventMessageScope)scopeValue;
                }

                var trigger = new SpecialtyEventTriggerDescriptor
                {
                    Id = id,
                    ZoneGroupId = zoneGroupId,
                    Type = triggerType,
                    Value1 = value1,
                    Value2 = value2,
                    CheckTime = checkTime,
                    EventRate = eventRate,
                    EventTime = eventTime,
                    MessageScope = messageScope,
                    StartMessage = ReadEventMessage(reader, id, "msg_start"),
                    EndMessage = ReadEventMessage(reader, id, "msg_end"),
                    SubjectId = subjectId,
                    SubjectType = subjectType,
                    SubjectItemIds = subjectItemIds
                };
                if (!triggers.TryAdd(id, trigger))
                    throw new InvalidDataException($"specialty_event_triggers contains duplicate row id {id}.");
            }
        }

        var events = new Dictionary<uint, SpecialtyEventDescriptor>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, specialty_event_trigger_id, event_type, event_value, event_object_id, " +
                "event_object_type, tooltip_text FROM specialty_events ORDER BY id";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var id = ReadId(reader, "specialty_events", "id");
                var triggerId = ReadId(reader, "specialty_events", "specialty_event_trigger_id", id);
                if (!triggers.TryGetValue(triggerId, out var trigger))
                    throw new InvalidDataException(
                        $"specialty_events row {id} references missing specialty event trigger {triggerId}.");

                var typeValue = ReadInt32(reader, "specialty_events", "event_type", id);
                if (!Enum.IsDefined(typeof(SpecialtyEventType), typeValue))
                    throw new InvalidDataException($"specialty_events row {id} has unknown event_type {typeValue}.");
                var eventType = (SpecialtyEventType)typeValue;
                var eventValue = ReadInt32(reader, "specialty_events", "event_value", id);
                if (eventType is (SpecialtyEventType.SaleRate or SpecialtyEventType.OverchargeRate) && eventValue <= 0)
                    throw new InvalidDataException(
                        $"specialty_events row {id} has non-positive price multiplier {eventValue}.");
                var objectType = ParseEventObjectType(reader.GetString("event_object_type"), id);
                var objectId = ReadId(reader, "specialty_events", "event_object_id", id);
                var targetItemIds = ResolveEventItemIds(objectType, objectId, id);

                var specialtyEvent = new SpecialtyEventDescriptor
                {
                    Id = id,
                    Type = eventType,
                    Value = eventValue,
                    ObjectId = objectId,
                    ObjectType = objectType,
                    TooltipText = reader.IsDBNull("tooltip_text") ? null : reader.GetString("tooltip_text"),
                    Trigger = trigger,
                    TargetItemIds = targetItemIds
                };
                if (!events.TryAdd(id, specialtyEvent))
                    throw new InvalidDataException($"specialty_events contains duplicate row id {id}.");
            }
        }

        _specialtyEventCatalog = new SpecialtyEventCatalog(triggers, events);
    }

    private ReadOnlyCollection<uint> ResolveEventItemIds(
        SpecialtyEventObjectType objectType,
        uint objectId,
        uint rowId)
    {
        if (objectType == SpecialtyEventObjectType.Item)
        {
            if (itemManager.GetTemplate(objectId) == null)
                throw new InvalidDataException($"specialty_events row {rowId} references missing item {objectId}.");
            return Array.AsReadOnly(new[] { objectId });
        }

        return ResolveItemSet(objectId, "specialty_events", rowId);
    }

    private ReadOnlyCollection<uint> ResolveEventItemIds(
        SpecialtyEventTriggerSubjectType subjectType,
        uint subjectId,
        HashSet<uint> questContextGroupIds,
        uint rowId)
    {
        switch (subjectType)
        {
            case SpecialtyEventTriggerSubjectType.Item:
                if (itemManager.GetTemplate(subjectId) == null)
                    throw new InvalidDataException(
                        $"specialty_event_triggers row {rowId} references missing item {subjectId}.");
                return Array.AsReadOnly(new[] { subjectId });
            case SpecialtyEventTriggerSubjectType.ItemSet:
                return ResolveItemSet(subjectId, "specialty_event_triggers", rowId);
            case SpecialtyEventTriggerSubjectType.QuestContextGroup:
                if (!questContextGroupIds.Contains(subjectId))
                    throw new InvalidDataException(
                        $"specialty_event_triggers row {rowId} references missing quest context group {subjectId}.");
                return Array.AsReadOnly(Array.Empty<uint>());
            case SpecialtyEventTriggerSubjectType.EnumHonorPointWarState:
                if (subjectId > (uint)ZoneConflictType.Peace)
                    throw new InvalidDataException(
                        $"specialty_event_triggers row {rowId} references unknown honor point war state {subjectId}.");
                return Array.AsReadOnly(Array.Empty<uint>());
            default:
                throw new InvalidDataException(
                    $"specialty_event_triggers row {rowId} has unsupported subject type {subjectType}.");
        }
    }

    private ReadOnlyCollection<uint> ResolveItemSet(uint itemSetId, string table, uint rowId)
    {
        var itemSet = itemManager.GetItemSet(itemSetId);
        if (itemSet == null)
            throw new InvalidDataException($"{table} row {rowId} references missing item set {itemSetId}.");
        if (itemSet.Items.Count == 0)
            throw new InvalidDataException($"{table} row {rowId} references empty item set {itemSetId}.");
        foreach (var member in itemSet.Items.Values)
        {
            if (itemManager.GetTemplate(member.ItemId) == null)
                throw new InvalidDataException(
                    $"{table} row {rowId} references item set {itemSetId} with missing item {member.ItemId}.");
        }
        return Array.AsReadOnly(itemSet.Items.Values.Select(x => x.ItemId).Distinct().Order().ToArray());
    }

    private static SpecialtyEventObjectType ParseEventObjectType(string value, uint rowId) => value switch
    {
        "Item" => SpecialtyEventObjectType.Item,
        "ItemSet" => SpecialtyEventObjectType.ItemSet,
        _ => throw new InvalidDataException($"specialty_events row {rowId} has unknown event_object_type '{value}'.")
    };

    private static SpecialtyEventTriggerSubjectType ParseTriggerSubjectType(string value, uint rowId) => value switch
    {
        "Item" => SpecialtyEventTriggerSubjectType.Item,
        "ItemSet" => SpecialtyEventTriggerSubjectType.ItemSet,
        "QuestContextGroup" => SpecialtyEventTriggerSubjectType.QuestContextGroup,
        "EnumHonorPointWarState" => SpecialtyEventTriggerSubjectType.EnumHonorPointWarState,
        _ => throw new InvalidDataException(
            $"specialty_event_triggers row {rowId} has unknown trigger_subject_type '{value}'.")
    };

    private string ReadEventMessage(SQLiteWrapperReader reader, uint rowId, string column)
    {
        if (reader.IsDBNull(column))
            throw new InvalidDataException($"specialty_event_triggers row {rowId} has null {column}.");
        var fallback = reader.GetString(column);
        var message = localizationManager.Get("specialty_event_triggers", column, rowId, fallback);
        if (Encoding.UTF8.GetByteCount(message) > byte.MaxValue)
            throw new InvalidDataException(
                $"specialty_event_triggers row {rowId} has {column} longer than 255 bytes.");
        return message;
    }

    private static int ReadInt32(SQLiteWrapperReader reader, string table, string column, uint rowId)
    {
        if (reader.IsDBNull(column))
            throw new InvalidDataException($"{table} row {rowId} has null {column}.");
        var value = reader.GetInt64(column);
        if (value < int.MinValue || value > int.MaxValue)
            throw new InvalidDataException($"{table} row {rowId} has invalid {column} {value}.");
        return (int)value;
    }

    private static uint ReadId(
        SQLiteWrapperReader reader,
        string table,
        string column,
        uint? rowId = null,
        bool allowZero = false)
    {
        if (reader.IsDBNull(column))
            throw new InvalidDataException(
                rowId.HasValue
                    ? $"{table} row {rowId} has null {column}."
                    : $"{table} has null {column}.");
        var value = reader.GetInt64(column);
        if (value < (allowZero ? 0 : 1) || value > uint.MaxValue)
            throw new InvalidDataException(
                rowId.HasValue
                    ? $"{table} row {rowId} has invalid {column} {value}."
                    : $"{table} has invalid {column} {value}.");
        return (uint)value;
    }

    internal void RestoreMarketState()
    {
        lock (_marketLock)
        {
            var loaded = marketStore.Load();
            var state = loaded.Clone();
            var clamped = false;
            foreach (var contributions in state.MaterialContributions.Values)
                clamped |= ClampMaterialContributions(contributions, _goodsStockLimit);
            foreach (var key in state.CargoStock.Keys.ToArray())
            {
                if (state.CargoStock[key] <= _tradeGoodStockLimit)
                    continue;
                state.CargoStock[key] = _tradeGoodStockLimit;
                clamped = true;
            }
            foreach (var (itemId, ratios) in state.PriceRatios)
            foreach (var (zoneGroupId, ratio) in ratios)
            {
                if (zoneManager.GetZoneGroupById(zoneGroupId) == null ||
                    !_specialtyNpcs.Values.Any(outlet =>
                        (outlet.ZoneGroupId == 0 || outlet.ZoneGroupId == zoneGroupId) &&
                        TryGetAcceptedBundleItem(itemId, outlet.SpecialtyBundleId, out _)) ||
                    ratio < checked(_specialtyContentSettings.MinPriceRatio * RatioUnitsPerPercent) ||
                    ratio > checked(_specialtyContentSettings.MaxPriceRatio * RatioUnitsPerPercent))
                    throw new InvalidDataException($"Invalid persisted specialty route/ratio: item {itemId}, zone {zoneGroupId}, ratio {ratio}.");
            }

            foreach (var (itemId, remainders) in state.DemandRemainders)
            foreach (var (zoneGroupId, remainder) in remainders)
            {
                if (remainder < 0 || remainder >= _specialtyContentSettings.GoodsRatioCount)
                    throw new InvalidDataException(
                        $"Invalid persisted specialty demand remainder: item {itemId}, zone {zoneGroupId}, remainder {remainder}.");
            }

            foreach (var ((zoneGroupId, tagId), contributions) in state.MaterialContributions)
            {
                if (!TryGetTradeGoodCategory(zoneGroupId, out var categoryId) ||
                    !_tradeGoodsByCategory[categoryId].Any(good =>
                        _tradeGoodMaterialsByTradeGoodId[good.Id].Any(material => material.TagId == tagId)))
                    throw new InvalidDataException($"Invalid persisted cargo material: zone {zoneGroupId}, tag {tagId}.");
                foreach (var contribution in contributions)
                {
                    if (!_specialtyBundleItemsMapped.ContainsKey(contribution.ItemId) ||
                        !TryResolveTradeGoodMaterial(categoryId, contribution.ItemId, out _, out var material) ||
                        material.TagId != tagId)
                        throw new InvalidDataException(
                            $"Invalid persisted cargo contribution: zone {zoneGroupId}, tag {tagId}, item {contribution.ItemId}.");
                }
            }

            foreach (var (zoneGroupId, tradeGoodId) in state.CargoStock.Keys)
            {
                if (!TryGetTradeGoodCategory(zoneGroupId, out var categoryId) ||
                    !_tradeGoodsByCategory[categoryId].Any(good => good.Id == tradeGoodId))
                    throw new InvalidDataException($"Invalid persisted cargo stock: zone {zoneGroupId}, tradegood {tradeGoodId}.");
            }

            foreach (var key in state.Records.Keys)
            {
                if (!state.PriceRatios.TryGetValue(key.ItemId, out var ratios) || !ratios.ContainsKey(key.ZoneGroupId))
                    throw new InvalidDataException($"Orphan specialty history: item {key.ItemId}, zone {key.ZoneGroupId}.");
            }

            clamped |= ReconcileFifoDemandRemainders(state);

            if (clamped)
            {
                state.Revision = checked(loaded.Revision + 1);
                marketStore.Commit(new SpecialtyMarketWrite(loaded, state));
            }
            _market = state;
        }
    }

    private bool ReconcileFifoDemandRemainders(SpecialtyMarketState state)
    {
        if (_specialtyContentSettings == null)
            return false;

        var changed = false;
        var bucketSize = checked((uint)_specialtyContentSettings.GoodsRatioCount);
        foreach (var (itemId, ratios) in state.PriceRatios)
        {
            if (!state.DemandRemainders.TryGetValue(itemId, out var remainders))
                continue;

            foreach (var zoneGroupId in ratios.Keys)
            {
                if (!remainders.TryGetValue(zoneGroupId, out var remainder))
                    continue;

                var quantity = SumMaterialContributions(state.MaterialContributions
                    .Where(x => x.Key.ZoneGroupId == zoneGroupId)
                    .SelectMany(x => x.Value)
                    .Where(x => x.ItemId == itemId));
                if (quantity == 0 && remainder == 0)
                    continue;
                if (quantity == 0 &&
                    (!TryGetTradeGoodCategory(zoneGroupId, out var categoryId) ||
                     !TryResolveTradeGoodMaterial(categoryId, itemId, out _, out _)))
                    continue;

                var expected = checked((int)(quantity % bucketSize));
                if (remainder == expected)
                    continue;
                remainders[zoneGroupId] = expected;
                changed = true;
            }
        }
        return changed;
    }

    private static bool ClampMaterialContributions(
        List<SpecialtyMaterialContribution> contributions,
        uint limit)
    {
        ulong retained = 0;
        for (var index = 0; index < contributions.Count; index++)
        {
            var contribution = contributions[index];
            var capacity = (ulong)limit - retained;
            if (contribution.Amount <= capacity)
            {
                retained += contribution.Amount;
                continue;
            }

            if (capacity > 0)
                contributions[index] = new SpecialtyMaterialContribution(
                    contribution.Sequence,
                    contribution.ItemId,
                    (uint)capacity);
            else
                index--;
            contributions.RemoveRange(index + 1, contributions.Count - index - 1);
            return true;
        }
        return false;
    }

    // Mutations run on a private copy under _marketLock. Readers only see the committed snapshot.
    private SpecialtyMarketWrite PrepareMarketWrite(Action mutate)
    {
        var expected = _market;
        _market = expected.Clone();
        try
        {
            mutate();
            _market.Revision = checked(expected.Revision + 1);
            return new SpecialtyMarketWrite(expected, _market);
        }
        finally
        {
            _market = expected;
        }
    }

    private void CommitMarketWrite(SpecialtyMarketWrite write)
    {
        try
        {
            marketStore.Commit(write);
        }
        catch (SpecialtyMarketConflictException)
        {
            RestoreMarketAndEventRuntime();
            throw;
        }
        _market = write.Updated;
    }

    private void RestoreMarketAndEventRuntime()
    {
        RestoreMarketState();
        if (!_initialized || !SpecialtyEventsEnabled || _reconcilingMarketConflict)
            return;

        _reconcilingMarketConflict = true;
        try
        {
            RestoreStockEventActivations();
            ScheduleStockEventChecks(false);
        }
        catch (Exception exception)
        {
            ClearActiveStockEvents();
            ClearScheduledStockEventChecks();
            Logger.Error(exception, "Failed to reconcile specialty stock event runtime after a market reload");
        }
        finally
        {
            _reconcilingMarketConflict = false;
        }
    }

    internal void LoadFreshnessData(SqliteConnection connection)
    {
        _freshnessGroups = [];
        var rowIds = new HashSet<uint>();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, freshness_group_id, time, reward_rate, seller_share_ratio " +
            "FROM freshness_group_items ORDER BY freshness_group_id, time, id";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var rowId = reader.GetInt64("id");
            var freshnessGroupId = reader.GetInt64("freshness_group_id");
            var timeSeconds = reader.GetInt64("time");
            var rewardRate = reader.GetInt64("reward_rate");
            if (rowId <= 0 || rowId > uint.MaxValue)
                throw new InvalidDataException($"freshness_group_items has invalid row id {rowId}.");
            if (freshnessGroupId <= 0 || freshnessGroupId > uint.MaxValue)
                throw new InvalidDataException(
                    $"freshness_group_items row {rowId} has invalid freshness_group_id {freshnessGroupId}.");
            if (timeSeconds <= 0 || timeSeconds > uint.MaxValue)
                throw new InvalidDataException(
                    $"freshness_group_items row {rowId} has invalid time threshold {timeSeconds}.");
            if (rewardRate <= 0 || rewardRate > uint.MaxValue)
                throw new InvalidDataException(
                    $"freshness_group_items row {rowId} has invalid reward_rate {rewardRate}.");

            var row = new FreshnessGroupItem
            {
                Id = (uint)rowId,
                FreshnessGroupId = (uint)freshnessGroupId,
                TimeSeconds = (uint)timeSeconds,
                RewardRate = (uint)rewardRate,
                SellerShareRatio = reader.IsDBNull("seller_share_ratio")
                    ? null
                    : reader.GetInt32("seller_share_ratio")
            };

            if (!rowIds.Add(row.Id))
                throw new InvalidDataException($"freshness_group_items contains duplicate row id {row.Id}.");
            if (row.SellerShareRatio is < 0 or > 10)
                throw new InvalidDataException(
                    $"freshness_group_items row {row.Id} has invalid seller_share_ratio {row.SellerShareRatio}.");

            if (!_freshnessGroups.TryGetValue(row.FreshnessGroupId, out var group))
            {
                group = [];
                _freshnessGroups.Add(row.FreshnessGroupId, group);
            }
            if (group.Count > 0 && group[^1].TimeSeconds >= row.TimeSeconds)
                throw new InvalidDataException(
                    $"freshness_group_items group {row.FreshnessGroupId} thresholds are not strictly increasing at row {row.Id}.");
            group.Add(row);
        }

        if (_freshnessGroups.Count == 0)
            throw new InvalidDataException("freshness_group_items contains no usable groups.");

        foreach (var backpackTemplate in itemManager.GetAllItems().OfType<BackpackTemplate>())
        {
            if (backpackTemplate.FreshnessGroupId != 0 &&
                !_freshnessGroups.ContainsKey(backpackTemplate.FreshnessGroupId))
                throw new InvalidDataException(
                    $"item_backpacks item {backpackTemplate.Id} references missing freshness group {backpackTemplate.FreshnessGroupId}.");
        }
    }

    internal static FreshnessGroupItem SelectFreshnessRow(
        IReadOnlyList<FreshnessGroupItem> freshnessRows,
        long elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(freshnessRows);
        if (freshnessRows.Count == 0)
            throw new ArgumentException("Freshness rows must not be empty.", nameof(freshnessRows));
        if (elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));

        foreach (var freshnessRow in freshnessRows)
        {
            if (elapsedSeconds <= freshnessRow.TimeSeconds)
                return freshnessRow;
        }
        return freshnessRows[^1];
    }

    internal static bool TrySelectFreshnessRow(
        Backpack backpack,
        IReadOnlyDictionary<uint, List<FreshnessGroupItem>> freshnessGroups,
        DateTime utcNow,
        out FreshnessGroupItem freshnessRow)
    {
        freshnessRow = null;
        if (backpack?.Template is not BackpackTemplate { FreshnessGroupId: > 0 } template ||
            freshnessGroups == null ||
            !freshnessGroups.TryGetValue(template.FreshnessGroupId, out var rows) ||
            rows.Count == 0 ||
            utcNow.Kind != DateTimeKind.Utc ||
            !backpack.TryGetFreshness(out var freshnessStartTime, out _) ||
            freshnessStartTime > utcNow)
            return false;

        var elapsedSeconds = checked((long)(utcNow - freshnessStartTime).TotalSeconds);
        freshnessRow = SelectFreshnessRow(rows, elapsedSeconds);
        return true;
    }

    internal void LoadSpecialtySaleData(SqliteConnection connection)
    {
        _specialtySaleSkill = skillManager.GetSkillTemplate(SkillsEnum.SellBackpack)
            ?? throw new InvalidDataException($"skills row {SkillsEnum.SellBackpack} is required for specialty sales.");
        if (_specialtySaleSkill.MaxRange <= 0)
            throw new InvalidDataException($"skills row {SkillsEnum.SellBackpack} has an invalid max_range.");
        if (_specialtySaleSkill.ConsumeLaborPower is < 0 or > short.MaxValue)
            throw new InvalidDataException(
                $"skills row {SkillsEnum.SellBackpack} has invalid consume_lp {_specialtySaleSkill.ConsumeLaborPower}.");
        if (_specialtySaleSkill.ConsumeLaborPower > 0 && _specialtySaleSkill.ActabilityGroupId <= 0)
            throw new InvalidDataException(
                $"skills row {SkillsEnum.SellBackpack} consumes labor without an actability_group_id.");
        RequireEnabledSkillRequirement(connection, SkillsEnum.SellBackpack);

        _specialtyContentSettings = new SpecialtyContentSettings
        {
            MaxPriceRatio = LoadRequiredContentValue(connection, "max_specialty_price_ratio"),
            MinPriceRatio = LoadRequiredContentValue(connection, "min_specialty_price_ratio"),
            AdjustRatioPerTrade = LoadRequiredContentValue(connection, "adjust_ratio_per_trade"),
            SellerShareRatio = LoadRequiredContentValue(connection, "seller_share_ratio"),
            SellBackpackLevelLimit = LoadRequiredContentValue(connection, "sell_backpack_level_limit"),
            PriceRecoverRate = LoadRequiredContentValue(connection, "specialty_price_recover_rate"),
            MailInterest = LoadRequiredContentValue(connection, "specialty_mail_interest"),
            GoodsRatioCount = LoadRequiredContentValue(connection, "specialty_goods_ratio_count")
        };

        if (_specialtyContentSettings.MinPriceRatio <= 0 ||
            _specialtyContentSettings.MaxPriceRatio < _specialtyContentSettings.MinPriceRatio ||
            _specialtyContentSettings.MaxPriceRatio > int.MaxValue / RatioUnitsPerPercent)
            throw new InvalidDataException(
                "content configs min_specialty_price_ratio/max_specialty_price_ratio define an invalid range.");
        if (_specialtyContentSettings.SellerShareRatio is < 1 or > 10)
            throw new InvalidDataException(
                $"content config 'seller_share_ratio' has invalid value {_specialtyContentSettings.SellerShareRatio}.");
        if (_specialtyContentSettings.SellBackpackLevelLimit < 0)
            throw new InvalidDataException(
                $"content config 'sell_backpack_level_limit' has negative value {_specialtyContentSettings.SellBackpackLevelLimit}.");
        if (_specialtyContentSettings.AdjustRatioPerTrade <= 0 ||
            _specialtyContentSettings.PriceRecoverRate is <= 0 or > 100 ||
            _specialtyContentSettings.MailInterest < 0 ||
            _specialtyContentSettings.GoodsRatioCount <= 0)
            throw new InvalidDataException("specialty content configs contain an invalid market or payout value.");
    }

    internal void LoadTradeGoodData(SqliteConnection connection)
    {
        _tradeGoods = [];
        _tradeGoodCategories = [];
        _tradeGoodsByCategory = [];
        _tradeGoodsByCategoryAndItem = [];
        _tradeGoodMaterialsByTradeGoodId = [];
        _tradeGoodPriceIndices = [];
        _tradeGoodMaterialContributions.Clear();
        _tradeGoodCargoStock.Clear();
        _tradeGoodSaleSkill = null;
        _tradeGoodSellLevelLimit = 0;
        _tradeGoodMailInterest = 0;
        _tradeGoodCoinPerGoldRatio = 0;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name FROM tradegood_categories";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var category = new TradeGoodCategory
                {
                    Id = reader.GetUInt32("id"),
                    Name = reader.GetString("name")
                };
                _tradeGoodCategories.Add(category.Id, category);
            }
        }
        if (_tradeGoodCategories.Count == 0)
            throw new InvalidDataException("tradegood_categories contains no rows.");

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, item_id, count, ratio, profit, tradegood_category_id, disp_order FROM tradegoods";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var id = reader.GetInt64("id");
                var outputCount = reader.GetInt64("count");
                if (id <= 0 || id > uint.MaxValue)
                    throw new InvalidDataException($"tradegoods has invalid row id {id}.");
                if (outputCount <= 0 || outputCount > uint.MaxValue)
                    throw new InvalidDataException($"tradegoods row {id} has invalid output count {outputCount}.");
                var tradeGood = new TradeGood
                {
                    Id = (uint)id,
                    ItemId = reader.GetUInt32("item_id"),
                    OutputCount = (uint)outputCount,
                    Ratio = reader.GetUInt32("ratio"),
                    Profit = reader.GetUInt32("profit"),
                    TradeGoodCategoryId = reader.GetUInt32("tradegood_category_id"),
                    DisplayOrder = reader.GetInt32("disp_order")
                };
                _tradeGoods.Add(tradeGood.Id, tradeGood);
            }
        }
        if (_tradeGoods.Count == 0)
            throw new InvalidDataException("tradegoods contains no rows.");

        var materialRowIds = new HashSet<uint>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, tradegood_id, tag_id, count FROM tradegood_materials";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var id = reader.GetInt64("id");
                var tradeGoodId = reader.GetInt64("tradegood_id");
                var tagId = reader.GetInt64("tag_id");
                var requiredCount = reader.GetInt64("count");
                if (id <= 0 || id > uint.MaxValue)
                    throw new InvalidDataException($"tradegood_materials has invalid row id {id}.");
                if (tradeGoodId <= 0 || tradeGoodId > uint.MaxValue)
                    throw new InvalidDataException(
                        $"tradegood_materials row {id} has invalid tradegood_id {tradeGoodId}.");
                if (tagId <= 0 || tagId > uint.MaxValue)
                    throw new InvalidDataException($"tradegood_materials row {id} has invalid tag_id {tagId}.");
                if (requiredCount <= 0 || requiredCount > uint.MaxValue)
                    throw new InvalidDataException($"tradegood_materials row {id} has invalid count {requiredCount}.");
                var material = new TradeGoodMaterial
                {
                    Id = (uint)id,
                    TradeGoodId = (uint)tradeGoodId,
                    TagId = (uint)tagId,
                    RequiredCount = (uint)requiredCount
                };
                if (!materialRowIds.Add(material.Id))
                    throw new InvalidDataException($"tradegood_materials contains duplicate row id {material.Id}.");
                if (!_tradeGoods.ContainsKey(material.TradeGoodId))
                    throw new InvalidDataException(
                        $"tradegood_materials row {material.Id} references missing tradegoods row {material.TradeGoodId}.");
                if (!_tradeGoodMaterialsByTradeGoodId.TryGetValue(material.TradeGoodId, out var materials))
                {
                    materials = [];
                    _tradeGoodMaterialsByTradeGoodId.Add(material.TradeGoodId, materials);
                }
                if (materials.Any(x => x.TagId == material.TagId))
                    throw new InvalidDataException(
                        $"tradegood_materials repeats tag {material.TagId} for tradegoods row {material.TradeGoodId}.");
                materials.Add(material);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT stock, price_index, charge FROM tradegood_priceindices ORDER BY stock ASC";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                _tradeGoodPriceIndices.Add(new TradeGoodPriceIndex
                {
                    Stock = reader.GetInt32("stock"),
                    PriceIndex = reader.GetUInt32("price_index"),
                    Charge = reader.GetUInt32("charge")
                });
            }
        }

        var fallbackCount = _tradeGoodPriceIndices.Count(x => x.Stock < 0);
        if (fallbackCount != 1)
            throw new InvalidDataException(
                $"tradegood_priceindices must contain exactly one negative fallback row; found {fallbackCount}.");
        var invalidCharge = _tradeGoodPriceIndices.FirstOrDefault(x => x.Charge == 0);
        if (invalidCharge != null)
            throw new InvalidDataException(
                $"tradegood_priceindices row with stock {invalidCharge.Stock} has a zero charge.");

        _tradeGoodInteractionSkill = skillManager.GetSkillTemplate(SkillsEnum.UseTradeGoodStore)
            ?? throw new InvalidDataException($"skills row {SkillsEnum.UseTradeGoodStore} is required for cargo interaction.");
        _tradeGoodSaleSkill = skillManager.GetSkillTemplate(SkillsEnum.SellTradeGood)
            ?? throw new InvalidDataException($"skills row {SkillsEnum.SellTradeGood} is required for cargo sales.");
        _tradeGoodPurchaseSkill = skillManager.GetSkillTemplate(SkillsEnum.BuyTradeGood)
            ?? throw new InvalidDataException($"skills row {SkillsEnum.BuyTradeGood} is required for cargo purchase.");
        if (_tradeGoodInteractionSkill.MaxRange <= 0)
            throw new InvalidDataException($"skills row {SkillsEnum.UseTradeGoodStore} has an invalid max_range.");
        if (_tradeGoodPurchaseSkill.MaxRange <= 0)
            throw new InvalidDataException($"skills row {SkillsEnum.BuyTradeGood} has an invalid max_range.");
        if (_tradeGoodSaleSkill.MaxRange <= 0)
            throw new InvalidDataException($"skills row {SkillsEnum.SellTradeGood} has an invalid max_range.");
        if (_tradeGoodSaleSkill.ConsumeLaborPower is < 0 or > short.MaxValue)
            throw new InvalidDataException(
                $"skills row {SkillsEnum.SellTradeGood} has invalid consume_lp {_tradeGoodSaleSkill.ConsumeLaborPower}.");
        if (_tradeGoodSaleSkill.ConsumeLaborPower > 0 && _tradeGoodSaleSkill.ActabilityGroupId <= 0)
            throw new InvalidDataException(
                $"skills row {SkillsEnum.SellTradeGood} consumes labor without an actability_group_id.");
        if (_tradeGoodPurchaseSkill.ConsumeLaborPower is < 0 or > short.MaxValue)
            throw new InvalidDataException(
                $"skills row {SkillsEnum.BuyTradeGood} has invalid consume_lp {_tradeGoodPurchaseSkill.ConsumeLaborPower}.");
        if (_tradeGoodPurchaseSkill.ConsumeLaborPower > 0 && _tradeGoodPurchaseSkill.ActabilityGroupId <= 0)
            throw new InvalidDataException(
                $"skills row {SkillsEnum.BuyTradeGood} consumes labor without an actability_group_id.");

        RequireEnabledSkillRequirement(connection, SkillsEnum.UseTradeGoodStore);
        RequireEnabledSkillRequirement(connection, SkillsEnum.SellTradeGood);
        RequireEnabledSkillRequirement(connection, SkillsEnum.BuyTradeGood);
        _tradeGoodSellLevelLimit = LoadRequiredContentValue(connection, "tradegoods_on_sell_level_limit");
        if (_tradeGoodSellLevelLimit < 0)
            throw new InvalidDataException(
                $"content config 'tradegoods_on_sell_level_limit' has negative value {_tradeGoodSellLevelLimit}.");
        _tradeGoodMailInterest = LoadRequiredContentValue(connection, "tradegoods_mail_interest");
        if (_tradeGoodMailInterest < 0)
            throw new InvalidDataException(
                $"content config 'tradegoods_mail_interest' has negative value {_tradeGoodMailInterest}.");
        _tradeGoodCoinPerGoldRatio = LoadRequiredContentValue(connection, "tradegoods_coin_per_gold_ratio");
        if (_tradeGoodCoinPerGoldRatio <= 0)
            throw new InvalidDataException(
                $"content config 'tradegoods_coin_per_gold_ratio' has non-positive value {_tradeGoodCoinPerGoldRatio}.");
        _tradeGoodBuyLevelLimit = LoadTradeGoodBuyLevelLimit(connection);
        _goodsStockLimit = LoadPositiveContentLimit(connection, "goods_stock_limit");
        _tradeGoodStockLimit = LoadPositiveContentLimit(connection, "tradegoods_stock_limit");
        if (_tradeGoodPriceIndices.Any(x => x.Stock >= _tradeGoodStockLimit))
            throw new InvalidDataException(
                $"tradegood_priceindices contains a threshold at or above tradegoods_stock_limit {_tradeGoodStockLimit}.");

        foreach (var tradeGood in _tradeGoods.Values)
        {
            if (!_tradeGoodCategories.ContainsKey(tradeGood.TradeGoodCategoryId))
                throw new InvalidDataException(
                    $"tradegoods row {tradeGood.Id} references missing tradegood_categories row {tradeGood.TradeGoodCategoryId}.");
            if (tradeGood.OutputCount == 0)
                throw new InvalidDataException($"tradegoods row {tradeGood.Id} has a zero output count.");
            if (!_tradeGoodMaterialsByTradeGoodId.TryGetValue(tradeGood.Id, out var materials) || materials.Count == 0)
                throw new InvalidDataException($"tradegoods row {tradeGood.Id} has no material recipe.");

            tradeGood.Item = itemManager.GetTemplate(tradeGood.ItemId);
            if (tradeGood.Item == null)
                throw new InvalidDataException(
                    $"tradegoods row {tradeGood.Id} references missing item template {tradeGood.ItemId}.");
            if (tradeGood.Item is not BackpackTemplate)
                throw new InvalidDataException(
                    $"tradegoods row {tradeGood.Id} item {tradeGood.ItemId} is not a backpack template.");

            ValidateMoneyPrice(connection, tradeGood);

            if (!_tradeGoodsByCategory.TryGetValue(tradeGood.TradeGoodCategoryId, out var categoryGoods))
            {
                categoryGoods = [];
                _tradeGoodsByCategory.Add(tradeGood.TradeGoodCategoryId, categoryGoods);
            }
            categoryGoods.Add(tradeGood);
            if (!_tradeGoodsByCategoryAndItem.TryAdd(
                    (tradeGood.TradeGoodCategoryId, tradeGood.ItemId),
                    tradeGood))
                throw new InvalidDataException(
                    $"tradegoods has duplicate item {tradeGood.ItemId} in category {tradeGood.TradeGoodCategoryId}.");
        }

        foreach (var categoryGoods in _tradeGoodsByCategory.Values)
        {
            var materialOwners = new Dictionary<uint, uint>();
            foreach (var tradeGood in categoryGoods)
            foreach (var material in _tradeGoodMaterialsByTradeGoodId[tradeGood.Id])
            {
                if (!materialOwners.TryAdd(material.TagId, tradeGood.Id))
                    throw new InvalidDataException(
                        $"tradegoods rows {materialOwners[material.TagId]} and {tradeGood.Id} in category {tradeGood.TradeGoodCategoryId} share material tag {material.TagId}.");
            }
        }

        foreach (var categoryGoods in _tradeGoodsByCategory.Values)
            categoryGoods.Sort((left, right) =>
            {
                var order = left.DisplayOrder.CompareTo(right.DisplayOrder);
                return order != 0 ? order : left.Id.CompareTo(right.Id);
            });
    }

    private static void RequireEnabledSkillRequirement(SqliteConnection connection, uint skillId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, enable FROM unit_reqs WHERE owner_type = 'Skill' AND owner_id = @skill_id";
        command.Parameters.AddWithValue("@skill_id", skillId);
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            if (reader.GetBoolean("enable"))
                return;
        }

        throw new InvalidDataException($"unit_reqs has no enabled row for skills row {skillId}.");
    }

    private static int LoadTradeGoodBuyLevelLimit(SqliteConnection connection)
    {
        var value = LoadRequiredContentValue(connection, "tradegoods_on_buy_level_limit");
        if (value < 0)
            throw new InvalidDataException(
                $"content config 'tradegoods_on_buy_level_limit' has negative value {value}.");
        return value;
    }

    private static uint LoadPositiveContentLimit(SqliteConnection connection, string name)
    {
        var value = LoadRequiredContentValue(connection, name);
        if (value <= 0)
            throw new InvalidDataException($"content config '{name}' has non-positive value {value}.");
        return checked((uint)value);
    }

    private static int LoadRequiredContentValue(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT c.value FROM content_configs c " +
            "JOIN enum_content_configs e ON e.id = c.id " +
            "WHERE e.name = @name";
        command.Parameters.AddWithValue("@name", name);
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        var values = new List<int>();
        while (reader.Read())
            values.Add(reader.GetInt32("value"));

        if (values.Count != 1)
            throw new InvalidDataException(
                $"content_configs/enum_content_configs must contain exactly one '{name}' row; found {values.Count}.");
        return values[0];
    }

    private void ValidateMoneyPrice(SqliteConnection connection, TradeGood tradeGood)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT price, refund FROM item_prices WHERE item_id = @item_id AND currency_id = @currency_id";
        command.Parameters.AddWithValue("@item_id", tradeGood.ItemId);
        command.Parameters.AddWithValue("@currency_id", (byte)ShopCurrencyType.Money);
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        var rows = 0;
        var price = 0;
        var refund = 0;
        while (reader.Read())
        {
            rows++;
            price = reader.GetInt32("price");
            refund = reader.GetInt32("refund");
        }

        if (rows != 1)
            throw new InvalidDataException(
                $"item_prices must contain exactly one money row for tradegoods item {tradeGood.ItemId}; found {rows}.");
        if (price < 0 || refund < 0)
            throw new InvalidDataException(
                $"item_prices money row for tradegoods item {tradeGood.ItemId} has negative price {price} or refund {refund}.");
        if (itemManager.GetShopPrice(tradeGood.ItemId, ShopCurrencyType.Money) != price ||
            tradeGood.Item.Price != price || tradeGood.Item.Refund != refund)
            throw new InvalidDataException(
                $"item_prices money row for tradegoods item {tradeGood.ItemId} did not resolve onto its item template.");
    }

    public void Initialize()
    {
        InitializeZoneConflictEvents();
        InitializeStockCountEvents();
        ClearTimedRatioRecoveryTask();

        var config = options.Value.Specialty;
        if (!config.EnableTimedRatioRecovery)
        {
            _initialized = true;
            return;
        }
        if (!double.IsFinite(config.RatioRecoveryIntervalMinutes) ||
            config.RatioRecoveryIntervalMinutes <= 0 ||
            config.RatioRecoveryIntervalMinutes > TimeSpan.MaxValue.TotalMinutes)
            throw new InvalidDataException(
                $"Specialty.RatioRecoveryIntervalMinutes must be a finite positive interval; got {config.RatioRecoveryIntervalMinutes}.");

        var interval = TimeSpan.FromMinutes(config.RatioRecoveryIntervalMinutes);
        if (interval <= TimeSpan.Zero)
            throw new InvalidDataException(
                $"Specialty.RatioRecoveryIntervalMinutes is below the minimum representable interval; got {config.RatioRecoveryIntervalMinutes}.");
        var task = new SpecialtyRatioRegenTask(this);
        if (!taskManager.Schedule(task, interval, interval))
            Logger.Error("Failed to schedule timed specialty ratio recovery");
        else
            _timedRatioRecoveryTask = task;
        _initialized = true;
    }

    private void ClearTimedRatioRecoveryTask()
    {
        if (_timedRatioRecoveryTask == null)
            return;
        taskManager.Cancel(_timedRatioRecoveryTask);
        _timedRatioRecoveryTask = null;
    }

    private void InitializeZoneConflictEvents()
    {
        if (!SpecialtyEventsEnabled)
            return;

        var subscribe = false;
        lock (_eventLock)
        {
            if (!_subscribedToZoneConflictStateChanges)
            {
                _subscribedToZoneConflictStateChanges = true;
                subscribe = true;
            }
        }

        if (subscribe)
            zoneManager.ZoneConflictStateChanged += OnZoneConflictStateChanged;
        ReconcileCurrentZoneConflictEvents();
    }

    private void InitializeStockCountEvents()
    {
        if (!SpecialtyEventsEnabled)
        {
            ClearScheduledStockEventChecks();
            return;
        }

        RestoreStockEventActivations();
        ScheduleStockEventChecks(true);
    }

    private void ScheduleStockEventChecks(bool persistCadence)
    {
        ClearScheduledStockEventChecks();

        var triggers = _specialtyEventCatalog.Events.Values
            .Where(x => x.Trigger.Type == SpecialtyEventTriggerType.StockCount)
            .Select(x => x.Trigger)
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.Id)
            .ToArray();
        var availableTriggers = new List<SpecialtyEventTriggerDescriptor>();
        foreach (var trigger in triggers)
        {
            if (!TryGetStockEventCargoStock(trigger, out _))
            {
                Logger.Warn(
                    "Skipping specialty stock trigger {0}: cargo item {1} is unavailable in ZoneGroup {2}",
                    trigger.Id,
                    trigger.SubjectId,
                    trigger.ZoneGroupId);
                continue;
            }
            availableTriggers.Add(trigger);
        }

        var triggerIds = availableTriggers.Select(x => x.Id).ToHashSet();
        var now = timeProvider.GetUtcNow();
        Dictionary<uint, long> nextChecks;
        lock (_marketLock)
        {
            var staleTriggerIds = _market.StockEventNextChecks.Keys.Where(x => !triggerIds.Contains(x)).ToArray();
            var missingTriggers = availableTriggers.Where(x => !_market.StockEventNextChecks.ContainsKey(x.Id)).ToArray();
            if (persistCadence && (staleTriggerIds.Length > 0 || missingTriggers.Length > 0))
            {
                var write = PrepareMarketWrite(() =>
                {
                    foreach (var triggerId in staleTriggerIds)
                        _market.StockEventNextChecks.Remove(triggerId);
                    foreach (var trigger in missingTriggers)
                        _market.StockEventNextChecks[trigger.Id] = now.AddSeconds(trigger.CheckTime).ToUnixTimeSeconds();
                });
                CommitMarketWrite(write);
            }
            nextChecks = new Dictionary<uint, long>(_market.StockEventNextChecks);
            foreach (var trigger in availableTriggers)
                nextChecks.TryAdd(trigger.Id, now.AddSeconds(trigger.CheckTime).ToUnixTimeSeconds());
        }

        foreach (var trigger in availableTriggers)
        {
            var interval = TimeSpan.FromSeconds(trigger.CheckTime);
            var delay = TimeSpan.FromSeconds(Math.Max(0, nextChecks[trigger.Id] - now.ToUnixTimeSeconds()));
            var task = new SpecialtyStockEventCheckTask(
                this,
                trigger.Id,
                Interlocked.Read(ref _stockEventScheduleGeneration));
            if (taskManager.Schedule(task, delay, interval))
            {
                lock (_eventLock)
                    _scheduledStockEventChecks[trigger.Id] = task;
                continue;
            }
            Logger.Error("Failed to schedule checks for specialty stock trigger {0}", trigger.Id);
        }
    }

    private bool TryGetStockEventCargoStock(SpecialtyEventTriggerDescriptor trigger, out uint stock)
    {
        lock (_marketLock)
        {
            stock = 0;
            if (!TryGetTradeGoodCategory(trigger.ZoneGroupId, out var categoryId) ||
                !_tradeGoodsByCategoryAndItem.TryGetValue((categoryId, trigger.SubjectId), out var tradeGood))
                return false;
            stock = _tradeGoodCargoStock.GetValueOrDefault((trigger.ZoneGroupId, tradeGood.Id));
            return true;
        }
    }

    private void ReconcileCurrentZoneConflictEvents()
    {
        foreach (var conflict in zoneManager.GetConflicts() ?? [])
        {
            ZoneConflictType observedState;
            do
            {
                observedState = conflict.CurrentZoneState;
                ReconcileZoneConflictEvents(conflict.ZoneGroupId, observedState);
            } while (conflict.CurrentZoneState != observedState);
        }
    }

    public void SendBuyList(Character player, uint npcObjId)
    {
        List<SpecialtyQuote> quotes;
        Npc npc;
        uint authoritativeZoneGroupId;
        uint categoryId;
        var eventSnapshot = CaptureSpecialtyEventSnapshot();
        lock (_marketLock)
        {
            if (!TryResolveTradeGoodOutlet(
                    player,
                    npcObjId,
                    _tradeGoodInteractionSkill,
                    true,
                    out npc,
                    out authoritativeZoneGroupId,
                    out categoryId))
                return;

            quotes = BuildBuyQuotes(categoryId, authoritativeZoneGroupId, eventSnapshot);
        }

        Logger.Debug(
            "Sending {0} cargo price rows for NPC {1}, category {2}, zone {3}, stock {4}, available: {5}",
            quotes.Count,
            npc.TemplateId,
            categoryId,
            authoritativeZoneGroupId,
            quotes.FirstOrDefault()?.Stock ?? 0,
            quotes.Any(x => x.CanProduce));
        SendGoodsPages(player, authoritativeZoneGroupId, quotes, eventSnapshot);
    }

    public void SendRatioList(Character player, ushort zoneGroupId, uint npcTemplateId)
    {
        var npc = player?.CurrentInteractionObject as Npc;
        if (player?.CurrentTarget is Npc currentTarget &&
            currentTarget.TemplateId == npcTemplateId &&
            currentTarget.ParentWorld == player.ParentWorld &&
            ReferenceEquals(player.ParentWorld.GetNpc(currentTarget.ObjId), currentTarget))
        {
            player.CurrentInteractionObject = currentTarget;
            npc = currentTarget;
        }

        if (!UsesSpecialtyPriceList(npc?.Template))
        {
            Logger.Warn(
                "Rejected specialty price request for character {0} ({1}): NPC template {2} is not a dedicated specialty buyer",
                player?.Id ?? 0,
                player?.ObjId ?? 0,
                npcTemplateId);
            player?.SendErrorMessage(ErrorMessageType.InvalidTarget);
            return;
        }

        SendSpecialtyPriceList(player, zoneGroupId, npcTemplateId);
    }

    internal static bool UsesSpecialtyPriceList(NpcTemplate npcTemplate) =>
        npcTemplate is { Specialty: true, TradeGoodBuy: false };

    private void SendSpecialtyPriceList(Character player, ushort zoneGroupId, uint npcTemplateId)
    {
        if (player?.CurrentInteractionObject is not Npc currentNpc ||
            !TryResolveSpecialtyOutlet(
                player,
                currentNpc.ObjId,
                player.ObjId,
                out var npc,
                out var specialtyNpc,
                out var authoritativeZoneGroupId))
            return;
        if (npcTemplateId != npc.TemplateId || zoneGroupId != authoritativeZoneGroupId)
        {
            Logger.Warn(
                "Rejected specialty price request for character {0} ({1}): requested NPC {2}/zone {3}, authoritative NPC {4}/zone {5}",
                player.Id,
                player.ObjId,
                npcTemplateId,
                zoneGroupId,
                npc.TemplateId,
                authoritativeZoneGroupId);
            player.SendErrorMessage(ErrorMessageType.InvalidTarget);
            return;
        }

        List<SpecialtyQuote> quotes;
        var eventSnapshot = CaptureSpecialtyEventSnapshot();
        var equippedBackpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        var acceptsEquippedBackpack = false;
        var quotedEquippedBackpack = false;
        lock (_marketLock)
        {
            quotes = BuildSellQuotes(specialtyNpc, authoritativeZoneGroupId, eventSnapshot);
            acceptsEquippedBackpack = equippedBackpack != null &&
                                       TryGetAcceptedBundleItem(
                                           equippedBackpack.TemplateId,
                                           specialtyNpc.SpecialtyBundleId,
                                           out _);
            quotedEquippedBackpack = equippedBackpack != null &&
                                     PrependCurrentSellQuote(quotes, equippedBackpack.TemplateId);
        }

        Logger.Debug(
            "Sending {0} specialty price rows for NPC {1}, bundle {2}, zone {3}",
            quotes.Count,
            npc.TemplateId,
            specialtyNpc.SpecialtyBundleId,
            authoritativeZoneGroupId);
        if (equippedBackpack != null)
        {
            Logger.Debug(
                "Specialty price context: equipped item {0} (instance {1}), backpack type {2}, accepted by bundle {3}: {4}, current quote prepended: {5}",
                equippedBackpack.TemplateId,
                equippedBackpack.Id,
                (equippedBackpack.Template as BackpackTemplate)?.BackpackType.ToString() ?? "not-backpack",
                specialtyNpc.SpecialtyBundleId,
                acceptsEquippedBackpack,
                quotedEquippedBackpack);
        }
        SendRatioPages(player, checked((ushort)authoritativeZoneGroupId), npc.TemplateId, quotes, eventSnapshot);
    }

    public bool CanStartTradeGoodInteraction(Character player, Npc npc)
    {
        lock (_marketLock)
        {
            return TryResolveTradeGoodOutlet(
                player,
                npc?.ObjId,
                _tradeGoodInteractionSkill,
                false,
                out _,
                out _,
                out _);
        }
    }

    public bool CanStartSpecialtyInteraction(Character player, Npc npc)
    {
        lock (_marketLock)
        {
            return TryResolveSpecialtyOutlet(
                player,
                npc?.ObjId ?? 0,
                player?.ObjId ?? 0,
                out _,
                out _,
                out _);
        }
    }

    private List<SpecialtyQuote> BuildSellQuotes(
        SpecialtyNpc specialtyNpc,
        uint zoneGroupId,
        SpecialtyEventSnapshot eventSnapshot) =>
        _specialtyBundleItems.Values
            .Where(x => x.SpecialtyBundleId == specialtyNpc.SpecialtyBundleId)
            .Where(x => x.Item != null)
            .OrderBy(x => x.ItemId)
            .Select(x => BuildSellQuote(x, zoneGroupId, eventSnapshot))
            .Where(x => x != null)
            .ToList();

    internal static bool PrependCurrentSellQuote(List<SpecialtyQuote> quotes, uint equippedItemId)
    {
        var quote = quotes.FirstOrDefault(x => x.ItemId == equippedItemId);
        if (quote == null)
            return false;
        quotes.Insert(0, quote);
        return true;
    }

    public bool BuySpecialty(
        Character player,
        uint npcObjId,
        SpecialtyQuote clientQuote)
    {
        if (player == null)
            return false;

        using var persistence = mailManager.DeferPersist();
        var broadcastRatios = false;
        lock (_marketLock)
            lock (player.StateSyncRoot)
            {
                if (!TryResolveTradeGoodOutlet(
                        player,
                        npcObjId,
                        _tradeGoodPurchaseSkill,
                        true,
                        out _,
                        out var zoneGroupId,
                        out var categoryId))
                    return false;

                if (player.Level < _tradeGoodBuyLevelLimit)
                {
                    player.SendErrorMessage(ErrorMessageType.SpecialtyNotBuyNow);
                    return false;
                }
                if (!player.Inventory.CanReplaceGliderInBackpackSlot())
                {
                    player.SendErrorMessage(ErrorMessageType.SpecialtyNotBuyNow);
                    return false;
                }
                if (!_tradeGoodsByCategoryAndItem.TryGetValue((categoryId, clientQuote.ItemId), out var tradeGood))
                {
                    player.SendErrorMessage(ErrorMessageType.SpecialtyNotBuyNow);
                    return false;
                }

                var stock = _tradeGoodCargoStock.GetValueOrDefault((zoneGroupId, tradeGood.Id));
                var authoritativeQuote = BuildBuyQuote(tradeGood, stock, zoneGroupId);
                if (!authoritativeQuote.CanProduce || !authoritativeQuote.Equals(clientQuote) ||
                    authoritativeQuote.Refund > long.MaxValue)
                {
                    player.SendErrorMessage(ErrorMessageType.SpecialtyNotBuyNow);
                    return false;
                }
                if (!TryGetTradeGoodPurchaseLaborCost(player, out var laborCost))
                    return false;
                if (player.LaborPower + player.LocalLaborPower < laborCost)
                {
                    player.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
                    return false;
                }
                var price = checked((long)authoritativeQuote.Refund);
                if (player.Money < price)
                {
                    player.SendErrorMessage(ErrorMessageType.NotEnoughMoney);
                    return false;
                }
                var previous = player.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack);
                var bagSlot = previous == null ? -1 : player.Inventory.Bag.GetUnusedSlot(-1);
                if (previous != null && bagSlot < 0)
                {
                    player.SendErrorMessage(ErrorMessageType.SpecialtyNotBuyNow);
                    return false;
                }

                var cargo = itemManager.CreateUnpersisted(authoritativeQuote.ItemId, 1, 0);
                if (cargo == null)
                    return false;
                SpecialtyPurchaseWrite write;
                var produced = 0u;
                try
                {
                    cargo.OwnerId = player.Id;
                    cargo.SlotType = SlotType.Equipment;
                    cargo.Slot = (int)EquipmentItemSlot.Backpack;
                    cargo._holdingContainer = player.Inventory.Equipment;
                    if (cargo.Template.BindType is ItemBindType.BindOnPickup or ItemBindType.BindOnEquip)
                        cargo.SetFlag(ItemFlag.SoulBound);
                    if (cargo.Template.ExpAbsLifetime > 0)
                        cargo.ExpirationTime = cargo.CreateTime.AddMinutes(cargo.Template.ExpAbsLifetime);
                    if (cargo.Template.ExpDate > DateTime.MinValue)
                        cargo.ExpirationTime = cargo.Template.ExpDate;
                    if (cargo.Template.ExpOnlineLifetime > 0)
                        cargo.ExpirationOnlineMinutesLeft = cargo.Template.ExpOnlineLifetime;

                    var expectedLabor = player.LaborPower;
                    var expectedLocalLabor = player.LocalLaborPower;
                    var fromAccount = Math.Min(laborCost, Math.Max(0, (int)expectedLabor));
                    var market = PrepareMarketWrite(() => produced = ConsumeTradeGoodCargoCore(zoneGroupId, tradeGood));
                    write = new SpecialtyPurchaseWrite(
                        player.Id, player.AccountId, player.Money, player.Money - price,
                        expectedLabor, expectedLabor - fromAccount,
                        expectedLocalLabor, expectedLocalLabor - (laborCost - fromAccount),
                        cargo, previous, player.Inventory.Bag.ContainerId, bagSlot, market,
                        player.Money2, itemManager.CaptureInventory(player.Id));
                    if (!purchaseStore.Commit(write))
                    {
                        itemManager.DiscardUnpersistedItems([cargo]);
                        RestoreMarketAfterRejectedPurchase();
                        player.SendErrorMessage(ErrorMessageType.SpecialtyNotBuyNow);
                        return false;
                    }
                }
                catch (Exception exception)
                {
                    itemManager.DiscardUnpersistedItems([cargo]);
                    Logger.Error(exception, "Failed to commit cargo purchase for character {0}", player.Id);
                    player.SendErrorMessage(ErrorMessageType.SpecialtyNotBuyNow);
                    return false;
                }

                PublishCommittedPurchase(player, write, laborCost);
                broadcastRatios = produced > 0;
            }
        if (broadcastRatios)
            BroadcastCurrentRatios();
        return true;
    }

    private void RestoreMarketAfterRejectedPurchase()
    {
        try
        {
            RestoreMarketAndEventRuntime();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to refresh specialty market after a rejected cargo purchase");
        }
    }

    private void PublishCommittedPurchase(Character player, SpecialtyPurchaseWrite write, int laborCost)
    {
        _market = write.Market.Updated;
        player.Money = write.NewMoney;
        var inventory = player.Inventory;
        var cargo = write.CargoItem;
        var previous = write.PreviousBackpack;
        // Reconcile persisted membership before any notification or equipment/quest callback can throw.
        if (previous != null)
        {
            inventory.Equipment.Items.Remove(previous);
            previous.SlotType = SlotType.Inventory;
            previous.Slot = write.BagSlot;
            previous._holdingContainer = inventory.Bag;
            inventory.Bag.Items.Add(previous);
            inventory.PreviousBackPackItemId = previous.Id;
            previous.IsDirty = false;
        }
        inventory.Equipment.Items.Add(cargo);
        inventory.Equipment.UpdateFreeSlotCount();
        inventory.Bag.UpdateFreeSlotCount();
        cargo.IsDirty = false;

        var publish = new List<Action>
        {
            () => itemManager.PublishPersistedItems([cargo]),
            () => player.ApplyCommittedLaborSpend(checked((short)laborCost),
                _tradeGoodPurchaseSkill.ActabilityGroupId, checked((short)write.NewLabor), write.NewLocalLabor)
        };
        if (previous != null)
        {
            publish.Add(() => inventory.Equipment.OnLeaveContainer(previous, inventory.Bag, (byte)EquipmentItemSlot.Backpack));
            publish.Add(() => inventory.Bag.OnEnterContainer(previous, inventory.Equipment, (byte)EquipmentItemSlot.Backpack));
            publish.Add(() => player.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreBuy,
                [new ItemMove(SlotType.Equipment, (byte)EquipmentItemSlot.Backpack, previous.Id,
                    SlotType.Inventory, checked((byte)write.BagSlot), 0)], [])));
        }
        publish.Add(() => inventory.Equipment.OnEnterContainer(cargo, null, 0));
        publish.Add(() => player.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.StoreBuy,
            [new MoneyChange(write.NewMoney - write.ExpectedMoney), new ItemAdd(cargo)], [])));
        publish.Add(() => inventory.OnAcquiredItem(cargo, 1));
        foreach (var action in publish)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Logger.Fatal(exception, "Failed to publish committed cargo item {0} for character {1}", cargo.Id, player.Id);
            }
        }
    }

    public bool SellSpecialty(Character player, uint npcObjId)
    {
        if (player == null)
            return false;

        using var persistence = mailManager.DeferPersist();
        // Cargo purchase already takes these locks in this order. Keep sale mutation
        // serialized with it and with all other state changes for this character.
        bool sold;
        uint soldToNpcTemplateId;
        uint soldBackpackTemplateId;
        lock (_marketLock)
            lock (player.StateSyncRoot)
                sold = SellSpecialtyLocked(player, npcObjId, out soldToNpcTemplateId, out soldBackpackTemplateId);
        if (sold)
        {
            BroadcastCurrentRatios();
            // Progress act QuestActObjSellBackpackGood, raised after the locks and only for a committed sale.
            player.Events?.OnSellBackpackGood(player, new OnSellBackpackGoodArgs
            {
                NpcTemplateId = soldToNpcTemplateId,
                BackpackTemplateId = soldBackpackTemplateId
            });
        }
        return sold;
    }

    private bool SellSpecialtyLocked(
        Character player,
        uint npcObjId,
        out uint npcTemplateId,
        out uint backpackTemplateId)
    {
        npcTemplateId = 0;
        backpackTemplateId = 0;
        if (!TryResolveSpecialtyOutlet(
                player,
                npcObjId,
                player?.ObjId ?? 0,
                out var npc,
                out var specialtyNpc,
                out var destinationZoneGroupId))
            return false;
        var backpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        if (backpack == null)
        {
            player.SendErrorMessage(ErrorMessageType.StoreBackpackNogoods);
            return false;
        }
        npcTemplateId = npc.TemplateId;
        backpackTemplateId = backpack.TemplateId;
        var isCargo = backpack.Template is BackpackTemplate { BackpackType: BackpackType.TradeGoods };
        var saleSkill = SelectSaleSkill(backpack.Template, _specialtySaleSkill, _tradeGoodSaleSkill);
        var sellLevelLimit = isCargo
            ? _tradeGoodSellLevelLimit
            : _specialtyContentSettings.SellBackpackLevelLimit;
        if (player.Level < sellLevelLimit)
        {
            player.SendErrorMessage(ErrorMessageType.SpecialtyNotBuyNow);
            return false;
        }
        var transactionUtc = timeProvider.GetUtcNow().UtcDateTime;
        FreshnessGroupItem freshnessRow = null;
        if (SpecialtyPackMaterializer.RequiresProductionContext(backpack.Template) &&
            (backpack is not Backpack freshnessBackpack ||
             !TrySelectFreshnessRow(freshnessBackpack, _freshnessGroups, transactionUtc, out freshnessRow)))
        {
            Logger.Error("Rejected specialty pack {0}: freshness detail is missing or invalid", backpack.Id);
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        if (!TryGetAcceptedBundleItem(backpack.TemplateId, specialtyNpc.SpecialtyBundleId, out var bundleItem) ||
            bundleItem.Item == null)
        {
            Logger.Warn(
                "Rejected specialty item {0} (instance {1}) for NPC {2}: bundle {3} does not accept it",
                backpack.TemplateId,
                backpack.Id,
                npc.TemplateId,
                specialtyNpc.SpecialtyBundleId);
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        var laborCost = saleSkill.ConsumeLaborPower;
        if (laborCost > 0)
        {
            if (!player.Actability.Actabilities.TryGetValue(
                    (uint)saleSkill.ActabilityGroupId,
                    out var commerce))
            {
                Logger.Error(
                    "Character {0} has no actability state for specialty sale skill {1} group {2}",
                    player.Id,
                    saleSkill.Id,
                    saleSkill.ActabilityGroupId);
                player.SendErrorMessage(ErrorMessageType.Invalid);
                return false;
            }
            laborCost = Math.Max(
                1,
                (int)Math.Round(
                    laborCost * commerce.GetLaborCostMultiplier(),
                    MidpointRounding.AwayFromZero));
        }
        // Both pools pay; see Character.ChangeLabor.
        if (player.LaborPower + player.LocalLaborPower < laborCost)
        {
            player.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
            return false;
        }

        var basePrice = GetBasePrice(bundleItem);
        var priceRatio = GetDisplayedRatioPercent(backpack.TemplateId, destinationZoneGroupId);
        if (basePrice <= 0)
        {
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }
        if (!EnsurePackPersisted(itemManager, backpack))
        {
            Logger.Error(
                "Failed to persist specialty pack {0} for character {1} before sale",
                backpack.Id,
                player.Id);
            player.SendErrorMessage(ErrorMessageType.MailUnknownFailure);
            return false;
        }

        SpecialtyMarketWrite marketWrite;
        try
        {
            marketWrite = PrepareSaleMarketWrite(backpack.TemplateId, destinationZoneGroupId);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to prepare specialty market delivery for pack {0}", backpack.Id);
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        var crafterId = backpack.MadeUnitId != player.Id ? backpack.MadeUnitId : 0;
        var eventMultiplier = GetActiveSpecialtyEventMultiplier(
            destinationZoneGroupId,
            backpack.TemplateId,
            SpecialtyEventType.OverchargeRate);
        var interestPercent = DecodeMailInterestPercent(
            isCargo ? _tradeGoodMailInterest : _specialtyContentSettings.MailInterest);
        var payout = CalculateSpecialtyPayout(
            basePrice,
            priceRatio,
            freshnessRow?.RewardRate ?? NeutralWireRatio,
            eventMultiplier,
            interestPercent);

        var itemTypeToDeliver = npc.Template.SpecialtyCoinId == 0 ? Item.Coins : npc.Template.SpecialtyCoinId;
        var totalPayout = npc.Template.SpecialtyCoinId == 0
            ? checked((int)payout.Total)
            : ConvertMoneyToTradeGoodCoinCount(payout.Total, _tradeGoodCoinPerGoldRatio);
        var sellerPayout = totalPayout;
        var crafterPayout = 0;
        var basePayout = basePrice;

        if (npc.Template.SpecialtyCoinId != 0)
        {
            basePayout = ConvertMoneyToTradeGoodCoinCount(basePrice, _tradeGoodCoinPerGoldRatio);
        }

        var sellerShareRatio = freshnessRow?.SellerShareRatio ?? _specialtyContentSettings.SellerShareRatio;
        if (crafterId != 0 && FeaturesManager.Fsets.BackpackProfitShare)
        {
            sellerPayout = checked((int)Math.Round(
                totalPayout * DecodeSellerShare(sellerShareRatio),
                MidpointRounding.AwayFromZero));
            crafterPayout = totalPayout - sellerPayout;
        }

        var mailPayoutBeforeInterest = npc.Template.SpecialtyCoinId == 0
            ? checked((int)payout.BeforeInterest)
            : ConvertMoneyToTradeGoodCoinCount(payout.BeforeInterest, _tradeGoodCoinPerGoldRatio);
        var mailTotalPayout = totalPayout;
        var freshnessPercent = freshnessRow?.RewardRate / 10d ?? 0d;
        var specialtyMerchantRatioPercent = GetSpecialtyMerchantRatioPercent(eventMultiplier);
        var sellerSharePercent = sellerShareRatio * 10;

        var payoutMails = new List<BaseMail>(2);
        if (sellerPayout > 0)
        {
            var sellerMail = new MailForSpeciality(
                player,
                crafterId,
                backpack.TemplateId,
                priceRatio,
                itemTypeToDeliver,
                basePayout,
                0,
                sellerPayout,
                crafterPayout,
                mailPayoutBeforeInterest,
                mailTotalPayout,
                transactionUtc,
                interestPercent,
                freshnessPercent,
                specialtyMerchantRatioPercent,
                sellerSharePercent);
            if (!sellerMail.FinalizeForSeller())
            {
                itemManager.DiscardUnpersistedItems(sellerMail.Body.Attachments);
                player.SendErrorMessage(ErrorMessageType.MailUnknownFailure);
                return false;
            }
            payoutMails.Add(sellerMail);
        }

        if (crafterPayout > 0)
        {
            var crafterMail = new MailForSpeciality(
                player,
                crafterId,
                backpack.TemplateId,
                priceRatio,
                itemTypeToDeliver,
                basePayout,
                0,
                sellerPayout,
                crafterPayout,
                mailPayoutBeforeInterest,
                mailTotalPayout,
                transactionUtc,
                interestPercent,
                freshnessPercent,
                specialtyMerchantRatioPercent,
                sellerSharePercent);
            if (!crafterMail.FinalizeForCrafter())
            {
                itemManager.DiscardUnpersistedItems(
                    payoutMails.SelectMany(mail => mail.Body.Attachments)
                        .Concat(crafterMail.Body.Attachments));
                player.SendErrorMessage(ErrorMessageType.MailUnknownFailure);
                return false;
            }
            payoutMails.Add(crafterMail);
        }

        if (!mailManager.TryPrepareBatch(payoutMails, out var preparedMails))
        {
            itemManager.DiscardUnpersistedItems(payoutMails.SelectMany(mail => mail.Body.Attachments));
            player.SendErrorMessage(ErrorMessageType.MailUnknownFailure);
            return false;
        }

        var payoutItems = payoutMails.SelectMany(mail => mail.Body.Attachments).ToList();
        var expectedLabor = player.LaborPower;
        var expectedLocalLabor = player.LocalLaborPower;
        var laborFromAccount = Math.Min(laborCost, Math.Max(0, (int)expectedLabor));
        var laborFromLocal = laborCost - laborFromAccount;
        var write = new SpecialtySaleWrite(
            backpack.Id,
            backpack.TemplateId,
            backpack.OwnerId,
            backpack._holdingContainer?.ContainerId ?? 0,
            backpack.SlotType,
            backpack.Slot,
            player.AccountId,
            expectedLabor,
            expectedLabor - laborFromAccount,
            expectedLocalLabor,
            expectedLocalLabor - laborFromLocal,
            payoutMails,
            marketWrite);
        var packWasDirty = backpack.IsDirty;
        backpack.IsDirty = false;

        SpecialtySaleCommitResult commitResult;
        try
        {
            commitResult = saleCommitter.Commit(
                write,
                () => PublishCommittedSale(
                    player,
                    backpack,
                    laborCost,
                    saleSkill.ActabilityGroupId,
                    write,
                    preparedMails),
                () =>
                {
                    backpack.IsDirty = packWasDirty;
                    mailManager.CancelPreparedBatch(preparedMails);
                    itemManager.DiscardUnpersistedItems(payoutItems);
                });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to persist specialty sale for pack {0} and character {1}", backpack.Id, player.Id);
            player.SendErrorMessage(ErrorMessageType.MailUnknownFailure);
            return false;
        }

        if (commitResult != SpecialtySaleCommitResult.Committed)
        {
            if (commitResult == SpecialtySaleCommitResult.MarketConflict)
            {
                try
                {
                    RestoreMarketAndEventRuntime();
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, "Failed to refresh specialty market after a rejected sale");
                }
            }
            Logger.Warn(
                "Rejected specialty sale for pack {0} and character {1}: {2}",
                backpack.Id,
                player.Id,
                commitResult);
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        return true;
    }

    internal SpecialtyMarketWrite PrepareSaleMarketWrite(uint itemId, uint destinationZoneGroupId)
    {
        lock (_marketLock)
            return PrepareMarketWrite(() =>
            {
                if (TryGetTradeGoodCategory(destinationZoneGroupId, out var categoryId) &&
                    TryResolveTradeGoodMaterial(categoryId, itemId, out _, out _))
                {
                    RecordTradeGoodDeliveryCore(destinationZoneGroupId, categoryId, itemId);
                    return;
                }

                RecordUnqueuedSpecialtyDelivery(itemId, destinationZoneGroupId);
            });
    }

    private void PublishCommittedSale(
        Character player,
        Item backpack,
        int laborCost,
        int actabilityGroupId,
        SpecialtySaleWrite write,
        PreparedMailBatch preparedMails)
    {
        _market = write.Market.Updated;
        try
        {
            if (!player.Inventory.Equipment.ConsumeCommittedItem(ItemTaskType.SellBackpack, backpack))
            {
                Logger.Fatal(
                    "Committed specialty pack {0} for character {1} could not be removed from live equipment state",
                    backpack.Id,
                    player.Id);
            }
        }
        catch (Exception exception)
        {
            Logger.Fatal(exception, "Failed to reconcile committed specialty pack {0} with live state", backpack.Id);
        }

        try
        {
            if (laborCost > 0)
            {
                player.ApplyCommittedLaborSpend(
                    checked((short)laborCost),
                    actabilityGroupId,
                    checked((short)write.NewLabor),
                    write.NewLocalLabor);
            }
        }
        catch (Exception exception)
        {
            Logger.Fatal(exception, "Failed to publish committed labor spend for specialty pack {0}", backpack.Id);
        }

        try
        {
            itemManager.PublishPersistedItems(preparedMails.Mails.SelectMany(mail => mail.Body.Attachments));
        }
        catch (Exception exception)
        {
            Logger.Fatal(exception, "Failed to publish committed specialty payout items for pack {0}", backpack.Id);
        }

        try
        {
            if (!mailManager.PublishPreparedBatch(preparedMails, true))
                Logger.Fatal("Committed specialty payout mail batch could not be published for pack {0}", backpack.Id);
        }
        catch (Exception exception)
        {
            Logger.Fatal(exception, "Failed to publish committed specialty payout mail batch for pack {0}", backpack.Id);
        }
    }

    internal static bool EnsurePackPersisted(IItemManager itemManager, Item backpack) =>
        !backpack.IsDirty || itemManager.TryPersistItem(backpack);

    public int GetRatioForSpecialty(Character player)
    {
        var backpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        if (backpack == null)
            return 0;
        var zoneGroupId = ZoneManager.Instance.GetZoneByKey(player.Transform.ZoneId)?.GroupId ?? 0;
        lock (_marketLock)
            return GetDisplayedRatioPercent(backpack.TemplateId, zoneGroupId);
    }

    public List<(uint, uint)> GetRatiosForTargetRoute(uint fromZoneGroupId, uint toZoneGroupId)
    {
        lock (_marketLock)
        {
            if (!_specialties.Values.Any(x =>
                    x.RowZoneGroupId == fromZoneGroupId && x.ColZoneGroupId == toZoneGroupId))
                return [];

            var results = itemManager.GetAllItems()
                .Where(x => x.SpecialtyZoneId == fromZoneGroupId)
                .OrderBy(x => x.Id)
                .Select(x =>
                {
                    var ratioUnits = GetRatioUnitsForItem(x.Id, toZoneGroupId);
                    return (x.Id, checked((uint)(ratioUnits / RatioUnitsPerWireUnit)));
                })
                .ToList();

            if (results.Count <= MaxCurrentRatios)
                return results;

            Logger.Error(
                "Specialty route {0}->{1} has {2} items; the native packet limit is {3}",
                fromZoneGroupId,
                toZoneGroupId,
                results.Count,
                MaxCurrentRatios);
            return results.Take(MaxCurrentRatios).ToList();
        }
    }

    public void SetTradeInfoSubscription(Character player, bool enter)
    {
        lock (_marketLock)
        {
            if (enter)
                _subscriptions.TryAdd(player.Id, []);
            else
                _subscriptions.Remove(player.Id);
        }
    }

    public void SendCurrentRatios(Character player, ushort fromZoneGroupId, ushort toZoneGroupId)
    {
        lock (_marketLock)
        {
            if (_subscriptions.TryGetValue(player.Id, out var routes))
                routes.Add((fromZoneGroupId, toZoneGroupId));
        }

        player.SendPacket(new SCSpecialtyCurrentPacket(
            fromZoneGroupId,
            toZoneGroupId,
            GetRatiosForTargetRoute(fromZoneGroupId, toZoneGroupId)));
    }

    public void SendRecords(Character player, ushort zoneGroupId, uint itemId)
    {
        List<SpecialtyMarketRecord> records;
        lock (_marketLock)
        {
            records = _records.TryGetValue((itemId, zoneGroupId), out var stored)
                ? stored.ToList()
                : [];
        }
        player.SendPacket(new SCSpecialtyRecordsPacket(zoneGroupId, itemId, records));
    }

    private bool TryResolveSpecialtyOutlet(
        Character player,
        uint? requestedNpcObjId,
        uint characterObjId,
        out Npc npc,
        out SpecialtyNpc specialtyNpc,
        out uint zoneGroupId)
    {
        npc = null;
        specialtyNpc = null;
        zoneGroupId = 0;
        if (player == null || characterObjId != player.ObjId || player.ParentWorld == null)
        {
            player?.SendErrorMessage(ErrorMessageType.InvalidTarget);
            return false;
        }

        if (player.CurrentInteractionObject is not Npc currentNpc ||
            currentNpc.ParentWorld != player.ParentWorld ||
            !ReferenceEquals(player.ParentWorld.GetNpc(currentNpc.ObjId), currentNpc))
        {
            player.SendErrorMessage(ErrorMessageType.InvalidTarget);
            return false;
        }

        if (requestedNpcObjId.HasValue && requestedNpcObjId.Value != currentNpc.ObjId)
        {
            player.SendErrorMessage(ErrorMessageType.InvalidTarget);
            return false;
        }

        if (currentNpc.Template?.Specialty != true ||
            !_specialtyNpcs.TryGetValue(currentNpc.TemplateId, out specialtyNpc))
        {
            player.SendErrorMessage(ErrorMessageType.InvalidTarget);
            return false;
        }
        npc = currentNpc;

        var equippedBackpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        var saleSkill = SelectSaleSkill(equippedBackpack?.Template, _specialtySaleSkill, _tradeGoodSaleSkill);
        if (saleSkill == null || player.GetDistanceTo(npc, true) > saleSkill.MaxRange)
        {
            player.SendErrorMessage(ErrorMessageType.TooFarAway);
            return false;
        }

        zoneGroupId = zoneManager.GetZoneByKey(npc.Transform.ZoneId)?.GroupId ?? 0;
        if (zoneGroupId == 0 ||
            specialtyNpc.ZoneGroupId != 0 && specialtyNpc.ZoneGroupId != zoneGroupId)
        {
            player.SendErrorMessage(ErrorMessageType.InvalidTarget);
            return false;
        }

        var requirement = UnitRequirementsGameData.Instance.CanUseSkill(
            saleSkill,
            player,
            new SkillCasterUnit(player.ObjId),
            new SkillCastUnitTarget(npc.ObjId));
        if (requirement.ResultKey != SkillResultKeys.ok)
        {
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        return true;
    }

    internal static SkillTemplate SelectSaleSkill(
        ItemTemplate itemTemplate,
        SkillTemplate specialtySaleSkill,
        SkillTemplate tradeGoodSaleSkill) =>
        itemTemplate is BackpackTemplate { BackpackType: BackpackType.TradeGoods }
            ? tradeGoodSaleSkill
            : specialtySaleSkill;

    internal static double DecodeMailInterestPercent(int contentValue) => contentValue / 10d;

    internal static double DecodeSellerShare(int contentValue) => contentValue / 10d;

    internal static (long BeforeInterest, long Total) CalculateSpecialtyPayout(
        int basePrice,
        int displayedRatioPercent,
        uint freshnessRewardRate,
        float eventMultiplier,
        double interestPercent)
    {
        if (basePrice < 0)
            throw new ArgumentOutOfRangeException(nameof(basePrice));
        if (displayedRatioPercent < 0)
            throw new ArgumentOutOfRangeException(nameof(displayedRatioPercent));
        if (freshnessRewardRate == 0)
            throw new ArgumentOutOfRangeException(nameof(freshnessRewardRate));
        if (!float.IsFinite(eventMultiplier) || eventMultiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(eventMultiplier));
        if (!double.IsFinite(interestPercent) || interestPercent < 0)
            throw new ArgumentOutOfRangeException(nameof(interestPercent));

        var beforeInterest =
            basePrice *
            (displayedRatioPercent / 100m) *
            (freshnessRewardRate / (decimal)NeutralWireRatio) *
            (decimal)eventMultiplier;
        var total = beforeInterest * (100m + (decimal)interestPercent) / 100m;
        return (
            checked((long)decimal.Round(beforeInterest, 0, MidpointRounding.AwayFromZero)),
            checked((long)decimal.Round(total, 0, MidpointRounding.AwayFromZero)));
    }

    internal static int ConvertMoneyToTradeGoodCoinCount(long moneyAmount, int moneyUnitsPerCoin)
    {
        if (moneyAmount < 0)
            throw new ArgumentOutOfRangeException(nameof(moneyAmount));
        if (moneyUnitsPerCoin <= 0)
            throw new ArgumentOutOfRangeException(nameof(moneyUnitsPerCoin));

        var rounded = MathF.Floor((float)moneyAmount / moneyUnitsPerCoin + 0.5f);
        if (!float.IsFinite(rounded) || rounded >= int.MaxValue)
            throw new OverflowException($"Specialty coin payout {rounded} is outside the supported range.");
        return (int)rounded;
    }

    private bool TryGetAcceptedBundleItem(
        uint itemId,
        uint specialtyBundleId,
        out SpecialtyBundleItem bundleItem)
    {
        bundleItem = null;
        return _specialtyBundleItemsMapped.TryGetValue(itemId, out var bundleMapping) &&
               bundleMapping.TryGetValue(specialtyBundleId, out bundleItem);
    }

    private bool TryResolveTradeGoodOutlet(
        Character player,
        uint? requestedNpcObjId,
        SkillTemplate requiredSkill,
        bool sendError,
        out Npc npc,
        out uint zoneGroupId,
        out uint categoryId)
    {
        npc = null;
        zoneGroupId = 0;
        categoryId = 0;
        if (player == null || player.ParentWorld == null)
            return false;
        if (player.CurrentInteractionObject is not Npc currentNpc ||
            currentNpc.ParentWorld != player.ParentWorld ||
            !ReferenceEquals(player.ParentWorld.GetNpc(currentNpc.ObjId), currentNpc))
            return RejectTradeGoodRequest(player, sendError, ErrorMessageType.InvalidTarget, "no live current NPC interaction");

        if (requestedNpcObjId.HasValue && requestedNpcObjId.Value != currentNpc.ObjId)
            return RejectTradeGoodRequest(player, sendError, ErrorMessageType.InvalidTarget, "NPC object id mismatch");
        if (!currentNpc.Template.TradeGoodBuy)
            return RejectTradeGoodRequest(player, sendError, ErrorMessageType.InvalidTarget, "NPC lacks the cargo vendor role");
        npc = currentNpc;
        if (requiredSkill == null)
            return RejectTradeGoodRequest(player, sendError, ErrorMessageType.SpecialtyNotBuyNow, "required cargo skill is unavailable");
        if (player.GetDistanceTo(npc, true) > requiredSkill.MaxRange)
            return RejectTradeGoodRequest(player, sendError, ErrorMessageType.TooFarAway, "NPC is outside interaction range");

        zoneGroupId = zoneManager.GetZoneByKey(npc.Transform.ZoneId)?.GroupId ?? 0;
        if (zoneGroupId == 0)
            return RejectTradeGoodRequest(player, sendError, ErrorMessageType.InvalidTarget, "zone group is unavailable");
        if (!TryGetTradeGoodCategory(zoneGroupId, out categoryId))
            return RejectTradeGoodRequest(
                player,
                sendError,
                ErrorMessageType.SpecialtyNotBuyNow,
                "zone group has no loaded cargo category");

        var requirement = UnitRequirementsGameData.Instance.CanUseSkill(
            requiredSkill,
            player,
            new SkillCasterUnit(player.ObjId),
            new SkillCastUnitTarget(npc.ObjId));
        if (requirement.ResultKey != SkillResultKeys.ok)
            return RejectTradeGoodRequest(
                player,
                sendError,
                ErrorMessageType.SpecialtyNotBuyNow,
                $"skill {requiredSkill.Id} requirement failed: {requirement.ResultKey}");

        return true;
    }

    private static bool RejectTradeGoodRequest(
        Character player,
        bool sendError,
        ErrorMessageType error,
        string reason)
    {
        Logger.Warn(
            "Rejected cargo request for character {0} ({1}): {2}",
            player?.Id ?? 0,
            player?.ObjId ?? 0,
            reason);
        if (sendError)
            player?.SendErrorMessage(error);
        return false;
    }

    private bool TryGetTradeGoodPurchaseLaborCost(Character player, out int laborCost)
    {
        laborCost = _tradeGoodPurchaseSkill.ConsumeLaborPower;
        if (laborCost <= 0)
            return true;

        var actabilityGroupId = _tradeGoodPurchaseSkill.ActabilityGroupId;
        if (actabilityGroupId > 0)
        {
            if (!player.Actability.Actabilities.TryGetValue((uint)actabilityGroupId, out var actability))
            {
                Logger.Error(
                    "Character {0} has no actability state for cargo purchase skill {1} group {2}",
                    player.Id,
                    _tradeGoodPurchaseSkill.Id,
                    actabilityGroupId);
                player.SendErrorMessage(ErrorMessageType.SpecialtyNotBuyNow);
                return false;
            }
            laborCost = checked((int)Math.Round(
                laborCost * actability.GetLaborCostMultiplier(),
                MidpointRounding.AwayFromZero));
        }

        laborCost = Math.Max(1, laborCost);
        return true;
    }

    private List<SpecialtyQuote> BuildBuyQuotes(
        uint categoryId,
        uint zoneGroupId,
        SpecialtyEventSnapshot eventSnapshot)
    {
        if (!_tradeGoodsByCategory.TryGetValue(categoryId, out var tradeGoods))
            return [];

        return tradeGoods
            .Select(tradeGood => BuildBuyQuote(
                tradeGood,
                _tradeGoodCargoStock.GetValueOrDefault((zoneGroupId, tradeGood.Id)),
                zoneGroupId,
                eventSnapshot))
            .ToList();
    }

    internal SpecialtyQuote BuildBuyQuote(TradeGood tradeGood, uint stock, uint zoneGroupId) =>
        BuildBuyQuote(tradeGood, stock, zoneGroupId, CaptureSpecialtyEventSnapshot());

    private SpecialtyQuote BuildBuyQuote(
        TradeGood tradeGood,
        uint stock,
        uint zoneGroupId,
        SpecialtyEventSnapshot eventSnapshot)
    {
        var priceIndex = SelectTradeGoodPriceIndex(_tradeGoodPriceIndices, stock);
        var rawPrice = tradeGood.Item.Refund + (tradeGood.Ratio / 1000f * tradeGood.Profit);
        var basePrice = RoundPositivePrice(rawPrice);
        var eventMultiplier = GetActiveSpecialtyEventMultiplier(
            eventSnapshot,
            zoneGroupId,
            tradeGood.ItemId,
            SpecialtyEventType.SaleRate);
        var currentPrice = RoundPositivePrice(
            rawPrice *
            (priceIndex.PriceIndex / 1000f) *
            (priceIndex.Charge / 1000f) *
            eventMultiplier);

        return new SpecialtyQuote
        {
            ItemId = tradeGood.ItemId,
            Refund = currentPrice,
            NoEventRefund = basePrice,
            Ratio = priceIndex.PriceIndex,
            Stock = stock,
            CanProduce = stock > 0,
            Currency = ShopCurrencyType.Money,
            Type = 0
        };
    }

    internal uint RecordTradeGoodDelivery(uint zoneGroupId, uint categoryId, uint itemId)
    {
        lock (_marketLock)
        {
            uint produced = 0;
            var write = PrepareMarketWrite(() => produced = RecordTradeGoodDeliveryCore(zoneGroupId, categoryId, itemId));
            CommitMarketWrite(write);
            return produced;
        }
    }

    private uint RecordTradeGoodDeliveryCore(uint zoneGroupId, uint categoryId, uint itemId)
    {
        lock (_marketLock)
        {
            if (!_tradeGoodsByCategory.ContainsKey(categoryId))
                return 0;

            if (!TryResolveTradeGoodMaterial(categoryId, itemId, out var matchedTradeGood, out var matchedMaterial))
            {
                Logger.Debug(
                    "Committed specialty item {0} does not match a cargo material in category {1}, zone {2}",
                    itemId,
                    categoryId,
                    zoneGroupId);
                return 0;
            }

            var quantitiesBefore = GetTradeGoodItemQuantities(zoneGroupId, matchedTradeGood);
            var materialKey = (zoneGroupId, matchedMaterial.TagId);
            var materialStock = GetMaterialStock(materialKey);
            var accepted = EnqueueMaterialContribution(materialKey, itemId, 1);
            var materialStockAfterDelivery = materialStock + accepted;
            var produced = ProduceAvailableTradeGoods(zoneGroupId, matchedTradeGood);
            ApplyDemandQuantityChanges(
                zoneGroupId,
                quantitiesBefore,
                GetTradeGoodItemQuantities(zoneGroupId, matchedTradeGood));
            Logger.Debug(
                "Counted specialty item {0} for cargo recipe {1} in zone {2}: tag {3} stock {4}/{5}, produced {6}, cargo stock {7}",
                itemId,
                matchedTradeGood.Id,
                zoneGroupId,
                matchedMaterial.TagId,
                materialStockAfterDelivery,
                matchedMaterial.RequiredCount,
                produced,
                _tradeGoodCargoStock.GetValueOrDefault((zoneGroupId, matchedTradeGood.Id)));
            return produced;
        }
    }

    private uint ProduceAvailableTradeGoods(uint zoneGroupId, TradeGood tradeGood)
    {
        var materials = _tradeGoodMaterialsByTradeGoodId[tradeGood.Id];
        var batches = materials.Min(material =>
            GetMaterialStock((zoneGroupId, material.TagId)) / material.RequiredCount);
        if (batches == 0)
            return 0;

        var cargoKey = (zoneGroupId, tradeGood.Id);
        var cargoStock = _tradeGoodCargoStock.GetValueOrDefault(cargoKey);
        if (cargoStock >= _tradeGoodStockLimit)
            return 0;

        var capacity = _tradeGoodStockLimit - cargoStock;
        var batchesForCapacity = capacity / tradeGood.OutputCount;
        batches = Math.Min(batches, batchesForCapacity);
        if (batches == 0)
            return 0;
        var produced = checked(batches * tradeGood.OutputCount);

        foreach (var material in materials)
        {
            var key = (zoneGroupId, material.TagId);
            ConsumeMaterialContributions(key, checked(batches * material.RequiredCount));
        }

        _tradeGoodCargoStock[cargoKey] = checked(cargoStock + produced);
        return produced;
    }

    private void RecordUnqueuedSpecialtyDelivery(uint itemId, uint zoneGroupId)
    {
        var maxRatioUnits = checked(_specialtyContentSettings.MaxPriceRatio * RatioUnitsPerPercent);
        if (!_priceRatios.TryGetValue(itemId, out var ratios))
            _priceRatios.Add(itemId, ratios = []);
        ratios.TryAdd(zoneGroupId, maxRatioUnits);
        if (!_demandRemainders.TryGetValue(itemId, out var remainders))
            _demandRemainders.Add(itemId, remainders = []);

        var deliveryCount = checked(remainders.GetValueOrDefault(zoneGroupId) + 1);
        var adjustments = deliveryCount / _specialtyContentSettings.GoodsRatioCount;
        remainders[zoneGroupId] = deliveryCount % _specialtyContentSettings.GoodsRatioCount;
        if (adjustments > 0)
        {
            var minRatioUnits = checked(_specialtyContentSettings.MinPriceRatio * RatioUnitsPerPercent);
            ratios[zoneGroupId] = Math.Max(
                minRatioUnits,
                ratios[zoneGroupId] - checked(adjustments * _specialtyContentSettings.AdjustRatioPerTrade));
        }
        RecordRatio(itemId, zoneGroupId, ratios[zoneGroupId]);
    }

    internal bool RecoverTimedRatios()
    {
        lock (_marketLock)
        {
            var maxRatioUnits = checked(_specialtyContentSettings.MaxPriceRatio * RatioUnitsPerPercent);
            var updates = new List<(uint ItemId, uint ZoneGroupId, int RatioUnits)>();
            foreach (var (itemId, byZone) in _priceRatios)
            foreach (var (zoneGroupId, ratioUnits) in byZone)
            {
                if (ratioUnits >= maxRatioUnits)
                    continue;
                var recovered = RecoverRatioUnits(
                    ratioUnits,
                    maxRatioUnits,
                    _specialtyContentSettings.PriceRecoverRate,
                    1);
                if (recovered != ratioUnits)
                    updates.Add((itemId, zoneGroupId, recovered));
            }

            if (updates.Count == 0)
                return false;

            var write = PrepareMarketWrite(() =>
            {
                foreach (var (itemId, zoneGroupId, ratioUnits) in updates)
                {
                    _priceRatios[itemId][zoneGroupId] = ratioUnits;
                    RecordRatio(itemId, zoneGroupId, ratioUnits);
                }
            });
            CommitMarketWrite(write);
        }

        BroadcastCurrentRatios();
        return true;
    }

    internal static int RecoverRatioUnits(int ratioUnits, int maxRatioUnits, int recoverRatePercent, uint steps)
    {
        var factor = (100m - recoverRatePercent) / 100m;
        var remainingFactor = 1m;
        var exponent = steps;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0)
                remainingFactor *= factor;
            factor *= factor;
            exponent >>= 1;
        }

        var recovered = maxRatioUnits - (maxRatioUnits - ratioUnits) * remainingFactor;
        var minimumRecovered = steps > 0 && recoverRatePercent > 0 && ratioUnits < maxRatioUnits
            ? checked(ratioUnits + 1)
            : ratioUnits;
        return Math.Clamp(
            checked((int)decimal.Round(recovered, 0, MidpointRounding.AwayFromZero)),
            minimumRecovered,
            maxRatioUnits);
    }

    internal uint GetTradeGoodMaterialStock(uint zoneGroupId, uint tagId)
    {
        lock (_marketLock)
            return GetMaterialStock((zoneGroupId, tagId));
    }

    internal uint GetTradeGoodCargoStock(uint zoneGroupId, uint tradeGoodId)
    {
        lock (_marketLock)
            return _tradeGoodCargoStock.GetValueOrDefault((zoneGroupId, tradeGoodId));
    }

    internal bool TryConsumeTradeGoodCargo(uint zoneGroupId, uint tradeGoodId)
    {
        var produced = 0u;
        lock (_marketLock)
        {
            var key = (zoneGroupId, tradeGoodId);
            var stock = _tradeGoodCargoStock.GetValueOrDefault(key);
            if (stock == 0 || !_tradeGoods.TryGetValue(tradeGoodId, out var tradeGood))
                return false;
            var write = PrepareMarketWrite(() => produced = ConsumeTradeGoodCargoCore(zoneGroupId, tradeGood));
            CommitMarketWrite(write);
        }

        if (produced > 0)
            BroadcastCurrentRatios();
        return true;
    }

    private uint ConsumeTradeGoodCargoCore(uint zoneGroupId, TradeGood tradeGood)
    {
        var key = (zoneGroupId, tradeGood.Id);
        var stock = _tradeGoodCargoStock.GetValueOrDefault(key);
        if (stock == 0)
            throw new InvalidOperationException($"Cargo recipe {tradeGood.Id} has no stock in zone {zoneGroupId}.");

        var quantitiesBefore = GetTradeGoodItemQuantities(zoneGroupId, tradeGood);
        _tradeGoodCargoStock[key] = stock - 1;
        var produced = ProduceAvailableTradeGoods(zoneGroupId, tradeGood);
        ApplyDemandQuantityChanges(
            zoneGroupId,
            quantitiesBefore,
            GetTradeGoodItemQuantities(zoneGroupId, tradeGood));
        return produced;
    }

    public (
        bool Success,
        string Error,
        uint TradeGoodId,
        uint Produced,
        uint CargoStock,
        IReadOnlyList<(uint TagId, uint Stock, uint RequiredCount)> Materials)
        AddTradeGoodMaterials(uint zoneGroupId, uint categoryId, IReadOnlyList<uint> requestedAmounts)
    {
        (bool Success, string Error, uint TradeGoodId, uint Produced, uint CargoStock,
            IReadOnlyList<(uint TagId, uint Stock, uint RequiredCount)> Materials) result = default;
        lock (_marketLock)
        {
            try
            {
                var write = PrepareMarketWrite(() => result = AddTradeGoodMaterialsCore(zoneGroupId, categoryId, requestedAmounts));
                if (result.Success)
                    CommitMarketWrite(write);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to persist cargo material grant for zone {0}", zoneGroupId);
                result = (false, "Cargo material grant could not be committed.", 0, 0, 0, []);
            }
        }
        if (result.Success)
            BroadcastCurrentRatios();
        return result;
    }

    private (
        bool Success,
        string Error,
        uint TradeGoodId,
        uint Produced,
        uint CargoStock,
        IReadOnlyList<(uint TagId, uint Stock, uint RequiredCount)> Materials)
        AddTradeGoodMaterialsCore(uint zoneGroupId, uint categoryId, IReadOnlyList<uint> requestedAmounts)
    {
        lock (_marketLock)
        {
            if (!_tradeGoodsByCategory.TryGetValue(categoryId, out var tradeGoods) || tradeGoods.Count == 0)
                return (false, $"Cargo category {categoryId} is not loaded.", 0, 0, 0, []);
            if (tradeGoods.Count != 1)
                return (false, $"Cargo category {categoryId} has {tradeGoods.Count} recipes; select a tradegood explicitly.", 0, 0, 0, []);

            var tradeGood = tradeGoods[0];
            var materials = _tradeGoodMaterialsByTradeGoodId[tradeGood.Id]
                .OrderBy(x => x.Id)
                .ToList();
            uint[] amounts;
            if (requestedAmounts == null || requestedAmounts.Count == 0)
                amounts = materials.Select(x => x.RequiredCount).ToArray();
            else if (requestedAmounts.Count == 1)
                amounts = Enumerable.Repeat(requestedAmounts[0], materials.Count).ToArray();
            else if (requestedAmounts.Count == materials.Count)
                amounts = requestedAmounts.ToArray();
            else
                return (
                    false,
                    $"Cargo recipe {tradeGood.Id} requires either one shared amount or {materials.Count} per-material amounts.",
                    tradeGood.Id,
                    0,
                    _tradeGoodCargoStock.GetValueOrDefault((zoneGroupId, tradeGood.Id)),
                    []);

            var representativeItems = new uint[materials.Count];
            for (var i = 0; i < materials.Count; i++)
            {
                var representativeItem = _specialtyBundleItemsMapped.Keys
                    .Order()
                    .FirstOrDefault(itemId =>
                        TryResolveTradeGoodMaterial(categoryId, itemId, out _, out var material) &&
                        material.TagId == materials[i].TagId);
                if (representativeItem == 0)
                    return (
                        false,
                        $"Cargo material tag {materials[i].TagId} has no loaded specialty item.",
                        tradeGood.Id,
                        0,
                        _tradeGoodCargoStock.GetValueOrDefault((zoneGroupId, tradeGood.Id)),
                        []);
                representativeItems[i] = representativeItem;
            }

            var quantitiesBefore = GetTradeGoodItemQuantities(zoneGroupId, tradeGood);
            for (var i = 0; i < materials.Count; i++)
            {
                var key = (zoneGroupId, materials[i].TagId);
                EnqueueMaterialContribution(key, representativeItems[i], amounts[i]);
            }

            var produced = ProduceAvailableTradeGoods(zoneGroupId, tradeGood);
            ApplyDemandQuantityChanges(
                zoneGroupId,
                quantitiesBefore,
                GetTradeGoodItemQuantities(zoneGroupId, tradeGood));
            var materialStocks = materials
                .Select(x => (
                    x.TagId,
                    GetMaterialStock((zoneGroupId, x.TagId)),
                    x.RequiredCount))
                .ToList();
            return (
                true,
                null,
                tradeGood.Id,
                produced,
                _tradeGoodCargoStock.GetValueOrDefault((zoneGroupId, tradeGood.Id)),
                materialStocks);
        }
    }

    internal SpecialtyQuote BuildSellQuote(SpecialtyBundleItem bundleItem, uint zoneGroupId) =>
        BuildSellQuote(bundleItem, zoneGroupId, CaptureSpecialtyEventSnapshot());

    private SpecialtyQuote BuildSellQuote(
        SpecialtyBundleItem bundleItem,
        uint zoneGroupId,
        SpecialtyEventSnapshot eventSnapshot)
    {
        var basePrice = GetBasePrice(bundleItem);
        if (basePrice <= 0)
            return null;
        var displayedRatioPercent = GetDisplayedRatioPercent(bundleItem.ItemId, zoneGroupId);
        var noEventPriceValue = basePrice * (displayedRatioPercent / 100d);
        var noEventPrice = checked((ulong)Math.Round(
            noEventPriceValue,
            MidpointRounding.AwayFromZero));
        var eventMultiplier = GetActiveSpecialtyEventMultiplier(
            eventSnapshot,
            zoneGroupId,
            bundleItem.ItemId,
            SpecialtyEventType.OverchargeRate);
        var currentPrice = checked((ulong)Math.Round(
            noEventPriceValue * eventMultiplier,
            MidpointRounding.AwayFromZero));
        var stock = GetMaterialStockForItem(zoneGroupId, bundleItem.ItemId);

        return new SpecialtyQuote
        {
            ItemId = bundleItem.ItemId,
            Refund = currentPrice,
            NoEventRefund = currentPrice == noEventPrice ? 0 : noEventPrice,
            Ratio = checked((uint)displayedRatioPercent),
            Stock = stock,
            CanProduce = true,
            Currency = ShopCurrencyType.Money,
            Type = 0
        };
    }

    private int GetBasePrice(SpecialtyBundleItem bundleItem)
    {
        return checked((int)(
            Math.Floor(bundleItem.Profit * (bundleItem.Ratio / (double)NeutralWireRatio)) +
            bundleItem.Item.Refund));
    }

    private bool TryGetTradeGoodCategory(uint zoneGroupId, out uint categoryId)
    {
        categoryId = 0;
        var factionChatRegionId = zoneManager.GetZoneGroupById(zoneGroupId)?.FactionChatRegionId ?? 0;
        var mappedCategoryId = GetTradeGoodCategoryId(factionChatRegionId);
        if (!mappedCategoryId.HasValue ||
            !_tradeGoodCategories.ContainsKey(mappedCategoryId.Value) ||
            !_tradeGoodsByCategory.ContainsKey(mappedCategoryId.Value))
            return false;
        categoryId = mappedCategoryId.Value;
        return true;
    }

    public static uint? GetTradeGoodCategoryId(uint factionChatRegionId)
    {
        if (factionChatRegionId is < FirstLandFactionChatRegionId or > LastLandFactionChatRegionId)
            return null;
        return 1u << checked((int)(factionChatRegionId - FirstLandFactionChatRegionId));
    }

    internal static TradeGoodPriceIndex SelectTradeGoodPriceIndex(
        IReadOnlyList<TradeGoodPriceIndex> priceIndices,
        uint stock)
    {
        var fallback = priceIndices.Single(x => x.Stock < 0);
        foreach (var priceIndex in priceIndices.Where(x => x.Stock >= 0).OrderBy(x => x.Stock))
        {
            if (stock <= priceIndex.Stock)
                return priceIndex;
        }
        return fallback;
    }

    private uint GetMaterialStockForItem(uint zoneGroupId, uint itemId)
    {
        if (!TryGetTradeGoodCategory(zoneGroupId, out var categoryId))
            return 0;

        if (!TryResolveTradeGoodMaterial(categoryId, itemId, out _, out var matched))
            return 0;
        return _tradeGoodMaterialContributions.TryGetValue((zoneGroupId, matched.TagId), out var contributions)
            ? SumMaterialContributions(contributions.Where(x => x.ItemId == itemId))
            : 0;
    }

    private Dictionary<uint, uint> GetTradeGoodItemQuantities(uint zoneGroupId, TradeGood tradeGood)
    {
        var quantities = new Dictionary<uint, uint>();
        foreach (var material in _tradeGoodMaterialsByTradeGoodId[tradeGood.Id])
        {
            if (!_tradeGoodMaterialContributions.TryGetValue((zoneGroupId, material.TagId), out var contributions))
                continue;
            foreach (var contribution in contributions)
                quantities[contribution.ItemId] = checked(
                    quantities.GetValueOrDefault(contribution.ItemId) + contribution.Amount);
        }
        return quantities;
    }

    private void ApplyDemandQuantityChanges(
        uint zoneGroupId,
        IReadOnlyDictionary<uint, uint> quantitiesBefore,
        IReadOnlyDictionary<uint, uint> quantitiesAfter)
    {
        if (_specialtyContentSettings == null)
            return;

        var maxRatioUnits = checked(_specialtyContentSettings.MaxPriceRatio * RatioUnitsPerPercent);
        var minRatioUnits = checked(_specialtyContentSettings.MinPriceRatio * RatioUnitsPerPercent);
        foreach (var itemId in quantitiesBefore.Keys.Union(quantitiesAfter.Keys))
        {
            var oldQuantity = quantitiesBefore.GetValueOrDefault(itemId);
            var newQuantity = quantitiesAfter.GetValueOrDefault(itemId);
            if (oldQuantity == newQuantity)
                continue;

            if (!_priceRatios.TryGetValue(itemId, out var ratios))
                _priceRatios.Add(itemId, ratios = []);
            var currentRatioUnits = ratios.GetValueOrDefault(zoneGroupId, maxRatioUnits);
            var oldBuckets = oldQuantity / checked((uint)_specialtyContentSettings.GoodsRatioCount);
            var newBuckets = newQuantity / checked((uint)_specialtyContentSettings.GoodsRatioCount);
            var bucketDelta = (long)newBuckets - oldBuckets;
            var adjustedRatioUnits = checked((int)Math.Clamp(
                currentRatioUnits - bucketDelta * _specialtyContentSettings.AdjustRatioPerTrade,
                minRatioUnits,
                maxRatioUnits));
            ratios[zoneGroupId] = adjustedRatioUnits;

            if (!_demandRemainders.TryGetValue(itemId, out var remainders))
                _demandRemainders.Add(itemId, remainders = []);
            remainders[zoneGroupId] = checked((int)(newQuantity % (uint)_specialtyContentSettings.GoodsRatioCount));
            RecordRatio(itemId, zoneGroupId, adjustedRatioUnits);
        }
    }

    private bool TryResolveTradeGoodMaterial(
        uint categoryId,
        uint itemId,
        out TradeGood matchedTradeGood,
        out TradeGoodMaterial matchedMaterial)
    {
        matchedTradeGood = null;
        matchedMaterial = null;
        if (!_tradeGoodsByCategory.TryGetValue(categoryId, out var tradeGoods))
            return false;

        foreach (var tradeGood in tradeGoods)
        foreach (var material in _tradeGoodMaterialsByTradeGoodId[tradeGood.Id])
        {
            if (!itemManager.HasItemTag(itemId, material.TagId))
                continue;
            if (matchedMaterial != null)
            {
                Logger.Error(
                    "Specialty item {0} matches multiple cargo material tags {1} and {2} in category {3}",
                    itemId,
                    matchedMaterial.TagId,
                    material.TagId,
                    categoryId);
                throw new InvalidDataException($"Specialty item {itemId} matches multiple material tags in category {categoryId}.");
            }
            matchedTradeGood = tradeGood;
            matchedMaterial = material;
        }
        return matchedMaterial != null;
    }

    private uint GetMaterialStock((uint ZoneGroupId, uint TagId) key)
    {
        if (!_tradeGoodMaterialContributions.TryGetValue(key, out var contributions))
            return 0;
        return SumMaterialContributions(contributions);
    }

    private static uint SumMaterialContributions(IEnumerable<SpecialtyMaterialContribution> contributions)
    {
        ulong total = 0;
        foreach (var contribution in contributions)
            total += contribution.Amount;
        return checked((uint)total);
    }

    private uint EnqueueMaterialContribution((uint ZoneGroupId, uint TagId) key, uint itemId, uint amount)
    {
        if (amount == 0)
            return 0;
        var stock = GetMaterialStock(key);
        amount = Math.Min(amount, _goodsStockLimit - Math.Min(stock, _goodsStockLimit));
        if (amount == 0)
            return 0;

        if (!_tradeGoodMaterialContributions.TryGetValue(key, out var contributions))
        {
            contributions = [];
            _tradeGoodMaterialContributions.Add(key, contributions);
        }
        if (contributions.Count > 0 && contributions[^1].ItemId == itemId)
        {
            var tail = contributions[^1];
            contributions[^1] = new SpecialtyMaterialContribution(tail.Sequence, itemId, checked(tail.Amount + amount));
            return amount;
        }

        var sequence = contributions.Count == 0 ? 1UL : checked(contributions[^1].Sequence + 1);
        contributions.Add(new SpecialtyMaterialContribution(sequence, itemId, amount));
        return amount;
    }

    private void ConsumeMaterialContributions((uint ZoneGroupId, uint TagId) key, uint amount)
    {
        if (!_tradeGoodMaterialContributions.TryGetValue(key, out var contributions) || GetMaterialStock(key) < amount)
            throw new InvalidDataException($"Insufficient cargo material stock: zone {key.ZoneGroupId}, tag {key.TagId}.");

        var remaining = amount;
        var consumedEntries = 0;
        while (remaining > 0)
        {
            var contribution = contributions[consumedEntries];
            if (contribution.Amount > remaining)
            {
                contributions[consumedEntries] = new SpecialtyMaterialContribution(
                    contribution.Sequence,
                    contribution.ItemId,
                    contribution.Amount - remaining);
                remaining = 0;
            }
            else
            {
                remaining -= contribution.Amount;
                consumedEntries++;
            }
        }

        if (consumedEntries > 0)
            contributions.RemoveRange(0, consumedEntries);
        if (contributions.Count == 0)
            _tradeGoodMaterialContributions.Remove(key);
    }

    private int GetRatioUnitsForItem(uint itemId, uint zoneGroupId)
    {
        return _priceRatios.TryGetValue(itemId, out var byZone) && byZone.TryGetValue(zoneGroupId, out var stored)
            ? stored
            : checked(_specialtyContentSettings.MaxPriceRatio * RatioUnitsPerPercent);
    }

    private int GetDisplayedRatioPercent(uint itemId, uint zoneGroupId) =>
        GetRatioUnitsForItem(itemId, zoneGroupId) / RatioUnitsPerPercent;

    private void RecordRatio(uint itemId, uint zoneGroupId, int ratioUnits)
    {
        var wireRatio = ratioUnits / RatioUnitsPerWireUnit;
        var key = (itemId, zoneGroupId);
        if (!_records.TryGetValue(key, out var records))
        {
            records = [];
            _records.Add(key, records);
        }
        if (records.Count > 0 && records[^1].Ratio == wireRatio)
            return;
        records.Add(new SpecialtyMarketRecord(wireRatio, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        if (records.Count > MaxHistoryRecords)
            records.RemoveRange(0, records.Count - MaxHistoryRecords);
    }

    private void BroadcastCurrentRatios()
    {
        List<(uint CharacterId, ushort From, ushort To)> deliveries;
        lock (_marketLock)
        {
            deliveries = _subscriptions
                .SelectMany(x => x.Value.Select(route => (x.Key, route.FromZoneGroupId, route.ToZoneGroupId)))
                .ToList();
        }

        foreach (var (characterId, from, to) in deliveries)
        {
            try
            {
                var player = WorldManager.Instance.GetCharacterById(characterId);
                if (player == null)
                {
                    lock (_marketLock)
                        _subscriptions.Remove(characterId);
                    continue;
                }
                player.SendPacket(new SCSpecialtyCurrentPacket(from, to, GetRatiosForTargetRoute(from, to)));
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to broadcast specialty ratios to character {0}", characterId);
            }
        }
    }

    private void SendRatioPages(
        Character player,
        ushort zoneGroupId,
        uint npcTemplateId,
        List<SpecialtyQuote> quotes,
        SpecialtyEventSnapshot eventSnapshot)
    {
        var eventIds = GetActiveSpecialtyEventIds(eventSnapshot, zoneGroupId, quotes.Select(x => x.ItemId).ToArray());
        var quotePageCount = (quotes.Count + QuotesPerPage - 1) / QuotesPerPage;
        var eventPageCount = (eventIds.Length + EventIdsPerPage - 1) / EventIdsPerPage;
        var pageCount = Math.Max(1, Math.Max(quotePageCount, eventPageCount));
        for (var page = 0; page < pageCount; page++)
        {
            var pageQuotes = quotes.Skip(page * QuotesPerPage).Take(QuotesPerPage).ToList();
            var pageEventIds = eventIds.Skip(page * EventIdsPerPage).Take(EventIdsPerPage).ToList();
            player.SendPacket(new SCSpecialtyRatioPacket(
                zoneGroupId,
                npcTemplateId,
                pageQuotes,
                pageEventIds,
                page == 0,
                page == pageCount - 1));
        }
    }

    private void SendGoodsPages(
        Character player,
        uint zoneGroupId,
        List<SpecialtyQuote> quotes,
        SpecialtyEventSnapshot eventSnapshot)
    {
        var eventIds = GetActiveSpecialtyEventIds(eventSnapshot, zoneGroupId, quotes.Select(x => x.ItemId).ToArray());
        var quotePageCount = (quotes.Count + QuotesPerPage - 1) / QuotesPerPage;
        var eventPageCount = (eventIds.Length + EventIdsPerPage - 1) / EventIdsPerPage;
        var pageCount = Math.Max(1, Math.Max(quotePageCount, eventPageCount));
        for (var page = 0; page < pageCount; page++)
        {
            var pageQuotes = quotes.Skip(page * QuotesPerPage).Take(QuotesPerPage).ToList();
            var pageEventIds = eventIds.Skip(page * EventIdsPerPage).Take(EventIdsPerPage).ToList();
            player.SendPacket(new SCSpecialtyGoodsPacket(
                pageQuotes,
                pageEventIds,
                page == 0,
                page == pageCount - 1));
        }
    }

    private static ulong RoundPositivePrice(float value)
    {
        const float SignedLongLimit = 9223372036854775808f;
        if (!float.IsFinite(value) || value < 0 || value >= SignedLongLimit)
            throw new OverflowException($"Specialty price {value} is outside the supported range.");
        return checked((ulong)MathF.Floor(value + 0.5f));
    }

    internal static int GetSpecialtyMerchantRatioPercent(float eventMultiplier)
    {
        if (eventMultiplier == 1f)
            return 0;
        return checked((int)MathF.Round(eventMultiplier * 100f, MidpointRounding.AwayFromZero));
    }

    private sealed record ActiveSpecialtyEventState(
        ActiveSpecialtyEvent Activation,
        long ActivationToken,
        SpecialtyEventExpiryTask ExpiryTask);

    private sealed record SpecialtyEventSnapshot(SpecialtyEventDescriptor[] Descriptors)
    {
        public static SpecialtyEventSnapshot Empty { get; } = new([]);
    }

    private readonly record struct SpecialtyEventRuntimePolicy(bool Enabled, uint DefaultManualDurationSeconds);
}
