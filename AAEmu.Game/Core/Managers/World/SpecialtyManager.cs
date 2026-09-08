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
    ISkillManager skillManager,
    IZoneManager zoneManager,
    IMailManager mailManager,
    SpecialtySaleCommitter saleCommitter,
    ISpecialtyMarketStore marketStore,
    ISpecialtyPurchaseStore purchaseStore,
    ITaskManager taskManager,
    IOptions<AppConfiguration> options) : Singleton<SpecialtyManager>, ISpecialtyManager
{
    private const int RatioUnitsPerPercent = 100;
    private const int RatioUnitsPerWireUnit = 10;
    private const uint NeutralWireRatio = 1000;
    private const uint MoneyUnitsPerCoin = 10000;
    private const uint FirstLandFactionChatRegionId = 2;
    private const uint LastLandFactionChatRegionId = 4;
    private const int MaxCurrentRatios = 128;
    private const int MaxHistoryRecords = 256;
    private const int QuotesPerPage = 20;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly object _marketLock = new();
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
    private int _tradeGoodBuyLevelLimit;

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
            _tradeGoodBuyLevelLimit = 0;
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

        foreach (var bundleItem in _specialtyBundleItems.Values)
        {
            bundleItem.Item = itemManager.GetTemplate(bundleItem.ItemId);
            if (bundleItem.Item == null)
                throw new InvalidDataException(
                    $"specialty_bundle_items row {bundleItem.Id} references missing item template {bundleItem.ItemId}.");
        }

        RestoreMarketState();

        Logger.Info(
            "Loaded {0} routes, {1} bundle items, {2} NPCs, {3} freshness groups, {4} cargo categories, {5} cargo goods and {6} price indices",
            _specialties.Count,
            _specialtyBundleItems.Count,
            _specialtyNpcs.Count,
            _freshnessGroups.Count,
            _tradeGoodCategories.Count,
            _tradeGoods.Count,
            _tradeGoodPriceIndices.Count);
    }

    internal void RestoreMarketState()
    {
        lock (_marketLock)
        {
            var state = marketStore.Load();
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

            foreach (var zoneGroupId in state.MaterialContributions.Keys.Select(x => x.ZoneGroupId).Distinct())
            {
                TryGetTradeGoodCategory(zoneGroupId, out var categoryId);
                foreach (var tradeGood in _tradeGoodsByCategory[categoryId])
                {
                    var complete = _tradeGoodMaterialsByTradeGoodId[tradeGood.Id].All(material =>
                        state.MaterialContributions.TryGetValue((zoneGroupId, material.TagId), out var contributions) &&
                        SumMaterialContributions(contributions) >= material.RequiredCount);
                    if (complete)
                        throw new InvalidDataException(
                            $"Persisted cargo contributions contain an unconverted recipe: zone {zoneGroupId}, tradegood {tradeGood.Id}.");
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
            _market = state;
        }
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
            RestoreMarketState();
            throw;
        }
        _market = write.Updated;
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
            PriceTradeGoodsCount = LoadRequiredContentValue(connection, "specialty_price_tradegoods_count"),
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
            _specialtyContentSettings.PriceTradeGoodsCount != 1 ||
            _specialtyContentSettings.PriceRecoverRate is <= 0 or > 100 ||
            _specialtyContentSettings.MailInterest < 0 ||
            _specialtyContentSettings.GoodsRatioCount != 4)
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
        _tradeGoodBuyLevelLimit = LoadTradeGoodBuyLevelLimit(connection);

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
        var config = options.Value.Specialty;
        if (!config.EnableTimedRatioRecovery)
            return;
        if (!double.IsFinite(config.RatioRecoveryIntervalMinutes) ||
            config.RatioRecoveryIntervalMinutes <= 0 ||
            config.RatioRecoveryIntervalMinutes > TimeSpan.MaxValue.TotalMinutes)
            throw new InvalidDataException(
                $"Specialty.RatioRecoveryIntervalMinutes must be a finite positive interval; got {config.RatioRecoveryIntervalMinutes}.");

        var interval = TimeSpan.FromMinutes(config.RatioRecoveryIntervalMinutes);
        if (interval <= TimeSpan.Zero)
            throw new InvalidDataException(
                $"Specialty.RatioRecoveryIntervalMinutes is below the minimum representable interval; got {config.RatioRecoveryIntervalMinutes}.");
        if (!taskManager.Schedule(new SpecialtyRatioRegenTask(this), interval, interval))
            Logger.Error("Failed to schedule timed specialty ratio recovery");
    }

    public void SendBuyList(Character player, uint npcObjId)
    {
        List<SpecialtyQuote> quotes;
        Npc npc;
        uint authoritativeZoneGroupId;
        uint categoryId;
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

            quotes = BuildBuyQuotes(categoryId, authoritativeZoneGroupId);
        }

        Logger.Debug(
            "Sending {0} cargo price rows for NPC {1}, category {2}, zone {3}, stock {4}, available: {5}",
            quotes.Count,
            npc.TemplateId,
            categoryId,
            authoritativeZoneGroupId,
            quotes.FirstOrDefault()?.Stock ?? 0,
            quotes.Any(x => x.CanProduce));
        SendGoodsPages(player, quotes);
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
        var equippedBackpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        var acceptsEquippedBackpack = false;
        var quotedEquippedBackpack = false;
        lock (_marketLock)
        {
            quotes = BuildSellQuotes(specialtyNpc, authoritativeZoneGroupId);
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
        SendRatioPages(player, checked((ushort)authoritativeZoneGroupId), npc.TemplateId, quotes);
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

    private List<SpecialtyQuote> BuildSellQuotes(SpecialtyNpc specialtyNpc, uint zoneGroupId) =>
        _specialtyBundleItems.Values
            .Where(x => x.SpecialtyBundleId == specialtyNpc.SpecialtyBundleId)
            .Where(x => x.Item != null)
            .OrderBy(x => x.ItemId)
            .Select(x => BuildSellQuote(x, zoneGroupId))
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
            var authoritativeQuote = BuildBuyQuote(tradeGood, stock);
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
            if (previous != null && (bagSlot < 0 || !EnsurePackPersisted(itemManager, previous)))
            {
                player.SendErrorMessage(ErrorMessageType.SpecialtyNotBuyNow);
                return false;
            }

            var cargo = itemManager.CreateUnpersisted(authoritativeQuote.ItemId, 1, 0);
            if (cargo == null)
                return false;
            SpecialtyPurchaseWrite write;
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
                var market = PrepareMarketWrite(() => _tradeGoodCargoStock[(zoneGroupId, tradeGood.Id)] = stock - 1);
                write = new SpecialtyPurchaseWrite(
                    player.Id, player.AccountId, player.Money, player.Money - price,
                    expectedLabor, expectedLabor - fromAccount,
                    expectedLocalLabor, expectedLocalLabor - (laborCost - fromAccount),
                    cargo, previous, player.Inventory.Bag.ContainerId, bagSlot, market);
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
            return true;
        }
    }

    private void RestoreMarketAfterRejectedPurchase()
    {
        try
        {
            RestoreMarketState();
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
        lock (_marketLock)
        lock (player.StateSyncRoot)
            sold = SellSpecialtyLocked(player, npcObjId);
        if (sold)
            BroadcastCurrentRatios();
        return sold;
    }

    private bool SellSpecialtyLocked(Character player, uint npcObjId)
    {
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
        var transactionUtc = DateTime.UtcNow;
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
        var priceRatioUnits = GetRatioUnitsForItem(backpack.TemplateId, destinationZoneGroupId);
        var priceRatio = priceRatioUnits / RatioUnitsPerPercent;
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
        var finalPriceNoInterest = basePrice * (priceRatioUnits / 10000d);
        var interestPercent = DecodeMailInterestPercent(
            isCargo ? _tradeGoodMailInterest : _specialtyContentSettings.MailInterest);
        var interest = finalPriceNoInterest * (interestPercent / 100d);
        var finalPrice = finalPriceNoInterest + interest;

        var itemTypeToDeliver = npc.Template.SpecialtyCoinId == 0 ? Item.Coins : npc.Template.SpecialtyCoinId;
        var totalPayout = checked((int)Math.Round(finalPrice, MidpointRounding.AwayFromZero));
        var sellerPayout = totalPayout;
        var crafterPayout = 0;
        var basePayout = basePrice;

        if (npc.Template.SpecialtyCoinId != 0)
        {
            totalPayout = checked((int)Math.Round(totalPayout / (double)MoneyUnitsPerCoin, MidpointRounding.AwayFromZero));
            sellerPayout = totalPayout;
            basePayout = checked((int)Math.Round(basePrice / (double)MoneyUnitsPerCoin, MidpointRounding.AwayFromZero));
        }

        var sellerShareRatio = freshnessRow?.SellerShareRatio ?? _specialtyContentSettings.SellerShareRatio;
        if (crafterId != 0 && FeaturesManager.Fsets.BackpackProfitShare)
        {
            sellerPayout = checked((int)Math.Round(
                totalPayout * DecodeSellerShare(sellerShareRatio),
                MidpointRounding.AwayFromZero));
            crafterPayout = totalPayout - sellerPayout;
        }

        var mailPayoutBeforeInterest = (int)(basePayout * (priceRatioUnits / 10000d));
        var mailTotalPayout = checked((int)Math.Round(
            mailPayoutBeforeInterest * (100d + interestPercent) / 100d,
            MidpointRounding.AwayFromZero));
        var freshnessPercent = freshnessRow?.RewardRate / 10d ?? 0d;
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
                    RestoreMarketState();
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
                if (!_priceRatios.TryGetValue(itemId, out var ratios))
                    _priceRatios.Add(itemId, ratios = []);
                var maxRatioUnits = checked(_specialtyContentSettings.MaxPriceRatio * RatioUnitsPerPercent);
                if (ratios.TryAdd(destinationZoneGroupId, maxRatioUnits))
                    RecordRatio(itemId, destinationZoneGroupId, maxRatioUnits);
                if (!_demandRemainders.TryGetValue(itemId, out var remainders))
                    _demandRemainders.Add(itemId, remainders = []);
                var deliveryCount = checked(remainders.GetValueOrDefault(destinationZoneGroupId) + 1);
                var adjustments = deliveryCount / _specialtyContentSettings.GoodsRatioCount;
                remainders[destinationZoneGroupId] = deliveryCount % _specialtyContentSettings.GoodsRatioCount;
                if (adjustments > 0)
                {
                    var minRatioUnits = checked(_specialtyContentSettings.MinPriceRatio * RatioUnitsPerPercent);
                    var adjustedRatio = Math.Max(
                        minRatioUnits,
                        ratios[destinationZoneGroupId] - checked(adjustments * _specialtyContentSettings.AdjustRatioPerTrade));
                    ratios[destinationZoneGroupId] = adjustedRatio;
                    RecordRatio(itemId, destinationZoneGroupId, adjustedRatio);
                }
                if (TryGetTradeGoodCategory(destinationZoneGroupId, out var categoryId))
                    RecordTradeGoodDeliveryCore(destinationZoneGroupId, categoryId, itemId);
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
            return GetRatioUnitsForItem(backpack.TemplateId, zoneGroupId) / RatioUnitsPerPercent;
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

    private List<SpecialtyQuote> BuildBuyQuotes(uint categoryId, uint zoneGroupId)
    {
        if (!_tradeGoodsByCategory.TryGetValue(categoryId, out var tradeGoods))
            return [];

        return tradeGoods
            .Select(tradeGood => BuildBuyQuote(
                tradeGood,
                _tradeGoodCargoStock.GetValueOrDefault((zoneGroupId, tradeGood.Id))))
            .ToList();
    }

    internal SpecialtyQuote BuildBuyQuote(TradeGood tradeGood, uint stock)
    {
        var priceIndex = SelectTradeGoodPriceIndex(_tradeGoodPriceIndices, stock);
        var basePrice = checked(
            (ulong)tradeGood.Item.Refund +
            (ulong)tradeGood.Profit * tradeGood.Ratio / NeutralWireRatio);
        var currentPrice = checked((ulong)decimal.Round(
            basePrice * (decimal)priceIndex.PriceIndex / priceIndex.Charge,
            0,
            MidpointRounding.AwayFromZero));

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

            var materialKey = (zoneGroupId, matchedMaterial.TagId);
            var materialStock = GetMaterialStock(materialKey);
            if (materialStock == uint.MaxValue)
            {
                Logger.Fatal(
                    "Cargo material stock reached its limit for zone {0}, tag {1}",
                    zoneGroupId,
                    matchedMaterial.TagId);
                throw new OverflowException($"Cargo material stock overflow: zone {zoneGroupId}, tag {matchedMaterial.TagId}.");
            }
            var materialStockAfterDelivery = materialStock + 1;
            EnqueueMaterialContribution(materialKey, itemId, 1);
            var produced = ProduceAvailableTradeGoods(zoneGroupId, matchedTradeGood);
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

        var produced = (ulong)batches * tradeGood.OutputCount;
        var cargoKey = (zoneGroupId, tradeGood.Id);
        var cargoStock = _tradeGoodCargoStock.GetValueOrDefault(cargoKey);
        if (produced > uint.MaxValue || produced > uint.MaxValue - cargoStock)
        {
            Logger.Fatal(
                "Cargo production exceeds stock capacity for zone {0}, tradegood {1}",
                zoneGroupId,
                tradeGood.Id);
            throw new OverflowException($"Cargo production overflow: zone {zoneGroupId}, tradegood {tradeGood.Id}.");
        }

        foreach (var material in materials)
        {
            var key = (zoneGroupId, material.TagId);
            ConsumeMaterialContributions(key, checked(batches * material.RequiredCount));
        }

        _tradeGoodCargoStock[cargoKey] = cargoStock + (uint)produced;
        RecoverSpecialtyRatios(zoneGroupId, tradeGood, (uint)produced);
        return (uint)produced;
    }

    private void RecoverSpecialtyRatios(uint zoneGroupId, TradeGood tradeGood, uint produced)
    {
        if (produced == 0 || _specialtyBundleItemsMapped.Count == 0)
            return;
        var recoverySteps = produced / checked((uint)_specialtyContentSettings.PriceTradeGoodsCount);
        if (recoverySteps == 0)
            return;

        var materialTags = _tradeGoodMaterialsByTradeGoodId[tradeGood.Id].Select(x => x.TagId).ToHashSet();
        var maxRatioUnits = checked(_specialtyContentSettings.MaxPriceRatio * RatioUnitsPerPercent);
        foreach (var itemId in _specialtyBundleItemsMapped.Keys)
        {
            if (!materialTags.Any(tagId => itemManager.HasItemTag(itemId, tagId)) ||
                !_priceRatios.TryGetValue(itemId, out var byZone) ||
                !byZone.TryGetValue(zoneGroupId, out var ratioUnits) ||
                ratioUnits >= maxRatioUnits)
                continue;

            var recovered = RecoverRatioUnits(
                ratioUnits,
                maxRatioUnits,
                _specialtyContentSettings.PriceRecoverRate,
                recoverySteps);
            byZone[zoneGroupId] = recovered;
            RecordRatio(itemId, zoneGroupId, recovered);
        }
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
        lock (_marketLock)
        {
            var key = (zoneGroupId, tradeGoodId);
            var stock = _tradeGoodCargoStock.GetValueOrDefault(key);
            if (stock == 0)
                return false;
            var write = PrepareMarketWrite(() => _tradeGoodCargoStock[key] = stock - 1);
            CommitMarketWrite(write);
            return true;
        }
    }

    internal (
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
        if (result.Success && result.Produced > 0)
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
                var key = (zoneGroupId, materials[i].TagId);
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
                if (amounts[i] > uint.MaxValue - GetMaterialStock(key))
                    return (
                        false,
                        $"Adding {amounts[i]} would overflow material tag {materials[i].TagId}.",
                        tradeGood.Id,
                        0,
                        _tradeGoodCargoStock.GetValueOrDefault((zoneGroupId, tradeGood.Id)),
                        []);
            }

            for (var i = 0; i < materials.Count; i++)
            {
                var key = (zoneGroupId, materials[i].TagId);
                EnqueueMaterialContribution(key, representativeItems[i], amounts[i]);
            }

            var produced = ProduceAvailableTradeGoods(zoneGroupId, tradeGood);
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

    internal SpecialtyQuote BuildSellQuote(SpecialtyBundleItem bundleItem, uint zoneGroupId)
    {
        var basePrice = GetBasePrice(bundleItem);
        if (basePrice <= 0)
            return null;
        var ratioUnits = GetRatioUnitsForItem(bundleItem.ItemId, zoneGroupId);
        var currentPrice = checked((ulong)Math.Round(
            basePrice * (ratioUnits / 10000d),
            MidpointRounding.AwayFromZero));
        var stock = GetMaterialStockForItem(zoneGroupId, bundleItem.ItemId);

        return new SpecialtyQuote
        {
            ItemId = bundleItem.ItemId,
            Refund = currentPrice,
            NoEventRefund = 0,
            Ratio = checked((uint)(ratioUnits / RatioUnitsPerPercent)),
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

    internal static uint? GetTradeGoodCategoryId(uint factionChatRegionId)
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

    private void EnqueueMaterialContribution((uint ZoneGroupId, uint TagId) key, uint itemId, uint amount)
    {
        if (amount == 0)
            return;
        var stock = GetMaterialStock(key);
        if (amount > uint.MaxValue - stock)
            throw new OverflowException($"Cargo material stock overflow: zone {key.ZoneGroupId}, tag {key.TagId}.");

        if (!_tradeGoodMaterialContributions.TryGetValue(key, out var contributions))
        {
            contributions = [];
            _tradeGoodMaterialContributions.Add(key, contributions);
        }
        if (contributions.Count > 0 && contributions[^1].ItemId == itemId)
        {
            var tail = contributions[^1];
            contributions[^1] = new SpecialtyMaterialContribution(tail.Sequence, itemId, checked(tail.Amount + amount));
            return;
        }

        var sequence = contributions.Count == 0 ? 1UL : checked(contributions[^1].Sequence + 1);
        contributions.Add(new SpecialtyMaterialContribution(sequence, itemId, amount));
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
            var player = WorldManager.Instance.GetCharacterById(characterId);
            if (player == null)
            {
                lock (_marketLock)
                    _subscriptions.Remove(characterId);
                continue;
            }
            player.SendPacket(new SCSpecialtyCurrentPacket(from, to, GetRatiosForTargetRoute(from, to)));
        }
    }

    private static void SendRatioPages(
        Character player,
        ushort zoneGroupId,
        uint npcTemplateId,
        List<SpecialtyQuote> quotes)
    {
        var pageCount = Math.Max(1, (quotes.Count + QuotesPerPage - 1) / QuotesPerPage);
        for (var page = 0; page < pageCount; page++)
        {
            var pageQuotes = quotes.Skip(page * QuotesPerPage).Take(QuotesPerPage).ToList();
            player.SendPacket(new SCSpecialtyRatioPacket(
                zoneGroupId,
                npcTemplateId,
                pageQuotes,
                [],
                page == 0,
                page == pageCount - 1));
        }
    }

    private static void SendGoodsPages(Character player, List<SpecialtyQuote> quotes)
    {
        var pageCount = Math.Max(1, (quotes.Count + QuotesPerPage - 1) / QuotesPerPage);
        for (var page = 0; page < pageCount; page++)
        {
            var pageQuotes = quotes.Skip(page * QuotesPerPage).Take(QuotesPerPage).ToList();
            player.SendPacket(new SCSpecialtyGoodsPacket(
                pageQuotes,
                [],
                page == 0,
                page == pageCount - 1));
        }
    }
}
