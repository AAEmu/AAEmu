using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Trading;
using AAEmu.Game.Models.Tasks.Specialty;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class SpecialtyManager(IItemManager itemManager) : Singleton<SpecialtyManager>, ISpecialtyManager
{
    private const uint WireRatioUnitsPerPercent = 10;
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
    private Dictionary<uint, TradeGood> _tradeGoods = [];
    private List<TradeGoodPriceIndex> _tradeGoodPriceIndices = [];

    // Specialty item -> destination zone group -> percentage (70-130 in the default configuration).
    private Dictionary<uint, Dictionary<uint, double>> _priceRatios = [];
    // Specialty item -> destination zone group -> packs delivered during the current ratio tick.
    private Dictionary<uint, Dictionary<uint, int>> _soldPackAmountInTick = [];
    // Specialty NPC template -> cargo-production stock at that outlet.
    private Dictionary<uint, uint> _tradeGoodStock = [];
    // Character id -> source/destination routes watched by the specialty information UI.
    private Dictionary<uint, HashSet<(ushort FromZoneGroupId, ushort ToZoneGroupId)>> _subscriptions = [];
    private Dictionary<(uint ItemId, uint ZoneGroupId), List<SpecialtyMarketRecord>> _records = [];

    public void Load()
    {
        lock (_marketLock)
        {
            _specialties = [];
            _specialtyBundleItems = [];
            _specialtyNpcs = [];
            _specialtyBundleItemsMapped = [];
            _tradeGoods = [];
            _tradeGoodPriceIndices = [];
            _priceRatios = [];
            _soldPackAmountInTick = [];
            _tradeGoodStock = [];
            _subscriptions = [];
            _records = [];
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

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, item_id, count, ratio, profit, tradegood_category_id, disp_order FROM tradegoods";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var tradeGood = new TradeGood
                {
                    Id = reader.GetUInt32("id"),
                    ItemId = reader.GetUInt32("item_id"),
                    Count = reader.GetUInt32("count"),
                    Ratio = reader.GetUInt32("ratio"),
                    Profit = reader.GetUInt32("profit"),
                    TradeGoodCategoryId = reader.GetUInt32("tradegood_category_id"),
                    DisplayOrder = reader.GetInt32("disp_order")
                };
                _tradeGoods.Add(tradeGood.Id, tradeGood);
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

        if (_tradeGoodPriceIndices.Count == 0 || _tradeGoodPriceIndices.All(x => x.Stock >= 0))
            throw new InvalidDataException("tradegood_priceindices must include a negative fallback row.");
        if (_tradeGoodPriceIndices.Any(x => x.Charge == 0))
            throw new InvalidDataException("tradegood_priceindices.charge must be non-zero.");

        foreach (var bundleItem in _specialtyBundleItems.Values)
            bundleItem.Item = itemManager.GetTemplate(bundleItem.ItemId);
        foreach (var tradeGood in _tradeGoods.Values)
            tradeGood.Item = itemManager.GetTemplate(tradeGood.ItemId);

        Logger.Info(
            "SpecialtyManager loaded {0} routes, {1} bundle items, {2} NPCs, {3} cargo goods and {4} price indices",
            _specialties.Count,
            _specialtyBundleItems.Count,
            _specialtyNpcs.Count,
            _tradeGoods.Count,
            _tradeGoodPriceIndices.Count);
    }

    public void Initialize()
    {
        var config = AppConfiguration.Instance.Specialty;
        TaskManager.Instance.Schedule(
            new SpecialtyRatioConsumeTask(),
            TimeSpan.FromMinutes(config.RatioDecreaseTickMinutes),
            TimeSpan.FromMinutes(config.RatioDecreaseTickMinutes));
        TaskManager.Instance.Schedule(
            new SpecialtyRatioRegenTask(),
            TimeSpan.FromMinutes(config.RatioRegenTickMinutes),
            TimeSpan.FromMinutes(config.RatioRegenTickMinutes));
    }

    public void SendBuyList(Character player, ushort zoneGroupId, uint npcTemplateId)
    {
        List<SpecialtyQuote> quotes;
        lock (_marketLock)
        {
            if (!_specialtyNpcs.TryGetValue(npcTemplateId, out var specialtyNpc) ||
                specialtyNpc.ZoneGroupId != zoneGroupId)
            {
                quotes = [];
            }
            else
            {
                quotes = BuildBuyQuotes(specialtyNpc);
            }
        }

        SendBuyPages(player, zoneGroupId, npcTemplateId, quotes);
    }

    public void SendSellList(Character player, uint npcObjId, uint characterObjId)
    {
        if (!TryGetOutlet(player, npcObjId, characterObjId, out _, out var specialtyNpc))
            return;

        List<SpecialtyQuote> quotes;
        lock (_marketLock)
        {
            quotes = _specialtyBundleItems.Values
                .Where(x => x.SpecialtyBundleId == specialtyNpc.SpecialtyBundleId && x.Item != null)
                .OrderBy(x => x.ItemId)
                .Select(x => BuildSellQuote(x, specialtyNpc.ZoneGroupId))
                .Where(x => x != null)
                .ToList();
        }

        SendSellPages(player, quotes);
    }

    public bool BuySpecialty(
        Character player,
        uint npcObjId,
        uint characterObjId,
        SpecialtyQuote clientQuote)
    {
        if (!TryGetOutlet(player, npcObjId, characterObjId, out _, out var specialtyNpc))
            return false;
        if (!player.Inventory.CanReplaceGliderInBackpackSlot())
        {
            player.SendErrorMessage(ErrorMessageType.SpecialtyNotBuyNow);
            return false;
        }

        lock (_marketLock)
        {
            var authoritativeQuote = BuildBuyQuotes(specialtyNpc)
                .FirstOrDefault(x => x.ItemId == clientQuote.ItemId);
            if (authoritativeQuote == null || !authoritativeQuote.CanProduce ||
                !authoritativeQuote.Equals(clientQuote) || authoritativeQuote.Refund > int.MaxValue)
            {
                player.SendErrorMessage(ErrorMessageType.SpecialtyNotBuyNow);
                return false;
            }

            var price = (int)authoritativeQuote.Refund;
            if (!player.ChangeMoney(SlotType.Inventory, SlotType.None, price, ItemTaskType.StoreBuy))
                return false;

            if (!player.Inventory.TryEquipNewBackPack(
                    ItemTaskType.StoreBuy,
                    authoritativeQuote.ItemId,
                    1))
            {
                player.AddMoney(SlotType.Inventory, price, ItemTaskType.StoreBuy);
                player.SendErrorMessage(ErrorMessageType.SpecialtyNotBuyNow);
                return false;
            }

            if (!TryGetTradeGoodForOutlet(specialtyNpc, out var tradeGood))
                throw new InvalidOperationException("The validated specialty quote no longer maps to a cargo good.");

            _tradeGoodStock[specialtyNpc.NpcId] -= tradeGood.Count;
            return true;
        }
    }

    public bool SellSpecialty(Character player, uint npcObjId, uint characterObjId)
    {
        if (!TryGetOutlet(player, npcObjId, characterObjId, out var npc, out var specialtyNpc))
            return false;

        var backpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        if (backpack == null)
        {
            player.SendErrorMessage(ErrorMessageType.StoreBackpackNogoods);
            return false;
        }

        if (!_specialtyBundleItemsMapped.TryGetValue(backpack.TemplateId, out var bundleMapping) ||
            !bundleMapping.TryGetValue(specialtyNpc.SpecialtyBundleId, out var bundleItem) ||
            bundleItem.Item == null)
        {
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        var commerce = player.Actability.Actabilities[(uint)ActabilityType.Commerce];
        var laborCost = Math.Max(
            1,
            (int)Math.Round(
                AppConfiguration.Instance.Specialty.SellLaborCost * commerce.GetLaborCostMultiplier(),
                MidpointRounding.AwayFromZero));
        if (player.LaborPower < laborCost)
        {
            player.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
            return false;
        }

        int priceRatio;
        int basePrice;
        lock (_marketLock)
        {
            basePrice = GetBasePrice(bundleItem);
            priceRatio = GetRatioForItem(backpack.TemplateId, specialtyNpc.ZoneGroupId);
        }
        if (basePrice <= 0)
        {
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        var crafterId = backpack.MadeUnitId != player.Id ? backpack.MadeUnitId : 0;
        var config = AppConfiguration.Instance.Specialty;
        var finalPriceNoInterest = basePrice * (priceRatio / 100d);
        var interest = finalPriceNoInterest * (config.InterestRate / 100d);
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

        if (crafterId != 0 && FeaturesManager.Fsets.BackpackProfitShare)
        {
            sellerPayout = checked((int)Math.Round(totalPayout * config.SellerShare, MidpointRounding.AwayFromZero));
            crafterPayout = totalPayout - sellerPayout;
        }

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
                config.InterestRate);
            sellerMail.FinalizeForSeller();
            if (!sellerMail.Send())
            {
                player.SendErrorMessage(ErrorMessageType.MailUnknownFailure);
                return false;
            }
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
                config.InterestRate);
            crafterMail.FinalizeForCrafter();
            if (!crafterMail.Send())
                player.SendErrorMessage(ErrorMessageType.MailUnknownFailure);
        }

        if (player.Inventory.Equipment.ConsumeItem(
                ItemTaskType.SellBackpack,
                backpack.TemplateId,
                1,
                backpack) != 1)
        {
            Logger.Error(
                "Failed to consume specialty pack {0} from character {1} after creating its payout mail",
                backpack.Id,
                player.Id);
            return false;
        }

        player.ChangeLabor((short)-laborCost, (int)ActabilityType.Commerce);

        lock (_marketLock)
        {
            if (!_soldPackAmountInTick.TryGetValue(backpack.TemplateId, out var byZone))
            {
                byZone = [];
                _soldPackAmountInTick.Add(backpack.TemplateId, byZone);
            }
            byZone.TryAdd(specialtyNpc.ZoneGroupId, 0);
            byZone[specialtyNpc.ZoneGroupId]++;

            if (TryGetTradeGoodForOutlet(specialtyNpc, out _))
            {
                _tradeGoodStock.TryAdd(specialtyNpc.NpcId, 0);
                _tradeGoodStock[specialtyNpc.NpcId]++;
            }
        }

        return true;
    }

    public int GetRatioForSpecialty(Character player)
    {
        var backpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        if (backpack == null)
            return 0;
        var zoneGroupId = ZoneManager.Instance.GetZoneByKey(player.Transform.ZoneId)?.GroupId ?? 0;
        lock (_marketLock)
            return GetRatioForItem(backpack.TemplateId, zoneGroupId);
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
                    var ratio = GetRatioForItem(x.Id, toZoneGroupId);
                    return (x.Id, checked((uint)ratio * WireRatioUnitsPerPercent));
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

    public void ConsumeRatio()
    {
        lock (_marketLock)
        {
            foreach (var (itemId, zoneInfo) in _soldPackAmountInTick)
            {
                foreach (var (zoneGroupId, count) in zoneInfo.ToList())
                {
                    if (count <= 0)
                        continue;

                    var ratioDecrease = Math.Ceiling(
                        count * AppConfiguration.Instance.Specialty.RatioDecreasePerPack);
                    var initialRatio = GetRatioForItem(itemId, zoneGroupId);
                    _soldPackAmountInTick[itemId][zoneGroupId] = 0;
                    var newRatio = Math.Max(
                        AppConfiguration.Instance.Specialty.MinSpecialtyRatio,
                        initialRatio - ratioDecrease);
                    _priceRatios[itemId][zoneGroupId] = newRatio;
                    RecordRatio(itemId, zoneGroupId, newRatio);
                }
            }
        }
        BroadcastCurrentRatios();
    }

    public void RegenRatio()
    {
        lock (_marketLock)
        {
            foreach (var (itemId, zoneInfo) in _soldPackAmountInTick)
            {
                foreach (var zoneGroupId in zoneInfo.Keys)
                {
                    var initialRatio = GetRatioForItem(itemId, zoneGroupId);
                    var newRatio = Math.Min(
                        AppConfiguration.Instance.Specialty.MaxSpecialtyRatio,
                        initialRatio + AppConfiguration.Instance.Specialty.RatioIncreasePerTick);
                    if (Math.Abs(newRatio - initialRatio) < double.Epsilon)
                        continue;
                    _priceRatios[itemId][zoneGroupId] = newRatio;
                    RecordRatio(itemId, zoneGroupId, newRatio);
                }
            }
        }
        BroadcastCurrentRatios();
    }

    private bool TryGetOutlet(
        Character player,
        uint npcObjId,
        uint characterObjId,
        out Npc npc,
        out SpecialtyNpc specialtyNpc)
    {
        npc = null;
        specialtyNpc = null;
        if (characterObjId != player.ObjId)
        {
            player.SendErrorMessage(ErrorMessageType.InvalidTarget);
            return false;
        }

        npc = player.ParentWorld.GetNpc(npcObjId);
        if (npc == null || !_specialtyNpcs.TryGetValue(npc.TemplateId, out specialtyNpc))
        {
            player.SendErrorMessage(ErrorMessageType.InvalidTarget);
            return false;
        }

        if (MathUtil.CalculateDistance(player.Transform.World.Position, npc.Transform.World.Position) >
            AppConfiguration.Instance.Specialty.InteractionRange)
        {
            player.SendErrorMessage(ErrorMessageType.TooFarAway);
            return false;
        }

        var actualZoneGroupId = ZoneManager.Instance.GetZoneByKey(npc.Transform.ZoneId)?.GroupId ?? 0;
        if (specialtyNpc.ZoneGroupId != 0 && specialtyNpc.ZoneGroupId != actualZoneGroupId)
        {
            player.SendErrorMessage(ErrorMessageType.InvalidTarget);
            return false;
        }

        return true;
    }

    private List<SpecialtyQuote> BuildBuyQuotes(SpecialtyNpc specialtyNpc)
    {
        if (!TryGetTradeGoodForOutlet(specialtyNpc, out var tradeGood) || tradeGood.Item == null)
            return [];

        var stock = _tradeGoodStock.GetValueOrDefault(specialtyNpc.NpcId);
        var priceIndex = GetTradeGoodPriceIndex(stock);
        var basePrice = checked((ulong)Math.Max(
            0,
            tradeGood.Item.Refund +
            Math.Floor(tradeGood.Profit * (tradeGood.Ratio / (double)NeutralWireRatio))));
        var currentPrice = checked((ulong)decimal.Round(
            basePrice * (decimal)priceIndex.PriceIndex / priceIndex.Charge,
            0,
            MidpointRounding.AwayFromZero));

        return
        [
            new SpecialtyQuote
            {
                ItemId = tradeGood.ItemId,
                Refund = currentPrice,
                NoEventRefund = basePrice,
                Ratio = priceIndex.PriceIndex,
                Stock = stock,
                CanProduce = stock >= tradeGood.Count,
                Currency = ShopCurrencyType.Money,
                Type = 0
            }
        ];
    }

    private SpecialtyQuote BuildSellQuote(SpecialtyBundleItem bundleItem, uint zoneGroupId)
    {
        var basePrice = GetBasePrice(bundleItem);
        if (basePrice <= 0)
            return null;
        var ratio = GetRatioForItem(bundleItem.ItemId, zoneGroupId);
        var wireRatio = checked((uint)ratio * WireRatioUnitsPerPercent);
        var currentPrice = checked((ulong)Math.Round(
            basePrice * (wireRatio / (double)NeutralWireRatio),
            MidpointRounding.AwayFromZero));
        var stock = _soldPackAmountInTick.TryGetValue(bundleItem.ItemId, out var byZone)
            ? checked((uint)Math.Max(0, byZone.GetValueOrDefault(zoneGroupId)))
            : 0;

        return new SpecialtyQuote
        {
            ItemId = bundleItem.ItemId,
            Refund = currentPrice,
            NoEventRefund = checked((ulong)basePrice),
            Ratio = wireRatio,
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

    private bool TryGetTradeGoodForOutlet(SpecialtyNpc specialtyNpc, out TradeGood tradeGood)
    {
        tradeGood = null;
        var factionChatRegionId = ZoneManager.Instance
            .GetZoneGroupById(specialtyNpc.ZoneGroupId)?.FactionChatRegionId ?? 0;
        // Content enum relationship: land chat regions 2, 3 and 4 map to tradegood categories
        // 1, 2 and 4 respectively. Region 1 is the sea and has no local cargo category.
        if (factionChatRegionId is < FirstLandFactionChatRegionId or > LastLandFactionChatRegionId)
            return false;
        var categoryId = 1u << checked((int)(factionChatRegionId - FirstLandFactionChatRegionId));
        tradeGood = _tradeGoods.Values.FirstOrDefault(x => x.TradeGoodCategoryId == categoryId);
        return tradeGood != null;
    }

    private TradeGoodPriceIndex GetTradeGoodPriceIndex(uint stock)
    {
        var fallback = _tradeGoodPriceIndices.First(x => x.Stock < 0);
        foreach (var priceIndex in _tradeGoodPriceIndices)
        {
            if (priceIndex.Stock >= 0 && stock <= priceIndex.Stock)
                return priceIndex;
        }
        return fallback;
    }

    private int GetRatioForItem(uint itemId, uint zoneGroupId)
    {
        if (!_priceRatios.TryGetValue(itemId, out var byZone))
        {
            byZone = [];
            _priceRatios.Add(itemId, byZone);
        }
        if (!byZone.TryGetValue(zoneGroupId, out var ratio))
        {
            ratio = AppConfiguration.Instance.Specialty.MaxSpecialtyRatio;
            byZone.Add(zoneGroupId, ratio);
            RecordRatio(itemId, zoneGroupId, ratio);
        }
        return (int)Math.Floor(ratio);
    }

    private void RecordRatio(uint itemId, uint zoneGroupId, double ratio)
    {
        var wireRatio = checked((int)Math.Floor(ratio * WireRatioUnitsPerPercent));
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

    private static void SendBuyPages(
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

    private static void SendSellPages(Character player, List<SpecialtyQuote> quotes)
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
