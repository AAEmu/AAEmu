using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Trading;
using AAEmu.Game.Models.Tasks.Specialty;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class SpecialtyManager : Singleton<SpecialtyManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, Specialty> _specialties;
    private Dictionary<uint, SpecialtyBundleItem> _specialtyBundleItems;
    private Dictionary<uint, SpecialtyNpc> _specialtyNpc;

    //                 itemId           bundleId
    private Dictionary<uint, Dictionary<uint, SpecialtyBundleItem>> _specialtyBundleItemsMapped;
    //                 itemId           zoneGroupId
    private Dictionary<uint, Dictionary<uint, double>> _priceRatios;
    //                 itemId           zoneId
    private Dictionary<uint, Dictionary<uint, int>> _soldPackAmountInTick;

    public void Load()
    {
        _specialties = new Dictionary<uint, Specialty>();
        _specialtyBundleItems = new Dictionary<uint, SpecialtyBundleItem>();
        _specialtyNpc = new Dictionary<uint, SpecialtyNpc>();
        _soldPackAmountInTick = new Dictionary<uint, Dictionary<uint, int>>();

        _specialtyBundleItemsMapped = new Dictionary<uint, Dictionary<uint, SpecialtyBundleItem>>();
        _priceRatios = new Dictionary<uint, Dictionary<uint, double>>();

        Logger.Info("SpecialtyManager is loading...");

        ItemManager.Instance.OnItemsLoaded += OnItemsLoaded;

        using (var connection = SQLite.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM specialties";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new Specialty();
                        template.Id = reader.GetUInt32("id");
                        template.RowZoneGroupId = reader.GetUInt32("row_zone_group_id");
                        template.ColZoneGroupId = reader.GetUInt32("col_zone_group_id");
                        //template.Ratio = reader.GetUInt32("ratio");
                        //template.Profit = reader.GetUInt32("profit");
                        //template.VendorExist = reader.GetBoolean("id", true);
                        _specialties.Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM specialty_bundle_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SpecialtyBundleItem();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.SpecialtyBundleId = reader.GetUInt32("specialty_bundle_id");
                        template.Profit = reader.GetUInt32("profit");
                        template.Ratio = reader.GetUInt32("ratio");
                        _specialtyBundleItems.Add(template.Id, template);

                        // Проверка на дубликат в _specialtyBundleItems
                        if (!_specialtyBundleItems.ContainsKey(template.Id))
                        {
                            _specialtyBundleItems.Add(template.Id, template);
                        }
                        else
                        {
                            Logger.Warn($"Duplicate found in _specialtyBundleItems: id={template.Id}");
                        }

                        // Проверка на дубликат в _specialtyBundleItemsMapped
                        if (!_specialtyBundleItemsMapped.ContainsKey(template.ItemId))
                            _specialtyBundleItemsMapped.Add(template.ItemId, new Dictionary<uint, SpecialtyBundleItem>());

                        if (!_specialtyBundleItemsMapped[template.ItemId].ContainsKey(template.SpecialtyBundleId))
                        {
                            _specialtyBundleItemsMapped[template.ItemId].Add(template.SpecialtyBundleId, template);
                        }
                        else
                        {
                            Logger.Warn($"Duplicate found in _specialtyBundleItemsMapped: itemId={template.ItemId}, specialtyBundleId={template.SpecialtyBundleId}");
                        }
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM specialty_npcs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SpecialtyNpc();
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        //template.Name = reader.GetString("name"); // there is no such field in the database for version 3.0.3.0
                        template.NpcId = reader.GetUInt32("npc_id");
                        template.SpecialtyBundleId = reader.GetUInt32("specialty_bundle_id");

                        // TODO есть повторы
                        // NpcId    SpecialtyBundleId
                        // 15086	25
                        // 15086	8000009
                        _specialtyNpc.TryAdd(template.NpcId, template);
                    }
                }
            }
        }

        Logger.Info("SpecialtyManager loaded");
    }

    public static void Initialize()
    {
        var ratioConsumeTask = new SpecialtyRatioConsumeTask();
        TaskManager.Instance.Schedule(ratioConsumeTask, TimeSpan.FromMinutes(AppConfiguration.Instance.Specialty.RatioDecreaseTickMinutes), TimeSpan.FromMinutes(AppConfiguration.Instance.Specialty.RatioDecreaseTickMinutes));

        var ratioRegenTask = new SpecialtyRatioRegenTask();
        TaskManager.Instance.Schedule(ratioRegenTask, TimeSpan.FromMinutes(AppConfiguration.Instance.Specialty.RatioRegenTickMinutes), TimeSpan.FromMinutes(AppConfiguration.Instance.Specialty.RatioRegenTickMinutes));
    }

    public void OnItemsLoaded(object sender, EventArgs e)
    {
        foreach (var specialtyBundleItem in _specialtyBundleItems.Values)
        {
            specialtyBundleItem.Item = ItemManager.Instance.GetTemplate(specialtyBundleItem.ItemId);
        }
    }

    /// <summary>
    /// Returns the Ration rounded down
    /// </summary>
    /// <param name="player"></param>
    /// <returns></returns>
    public int GetRatioForSpecialty(Character player)
    {
        var backpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        if (backpack == null)
            return 0;

        var zoneGroupId = ZoneManager.Instance.GetZoneByKey(player.Transform.ZoneId)?.GroupId ?? 0;

        InitRatioInZoneForPack(backpack.TemplateId, zoneGroupId);

        return (int)Math.Floor(_priceRatios[backpack.TemplateId][zoneGroupId]);
    }

    /// <summary>
    /// Gets a list of items and their current trade-rate for given zones
    /// </summary>
    /// <param name="fromZoneGroupId">Zone where the item was made</param>
    /// <param name="toZoneGroupId">Zone where the item is traded in</param>
    /// <returns></returns>
    public List<(uint, uint)> GetRatiosForTargetRoute(uint fromZoneGroupId, uint toZoneGroupId)
    {
        var res = new List<(uint, uint)>();

        // Get list of possible source packs
        var sourcePacks = ItemManager.Instance.GetAllItems().Where(x => x.SpecialtyZoneId == fromZoneGroupId);
        foreach (var item in sourcePacks)
        {
            InitRatioInZoneForPack(item.Id, toZoneGroupId);
            res.Add((item.Id, (uint)Math.Floor(_priceRatios[item.Id][toZoneGroupId])));
        }

        return res;
    }

    public int GetBasePriceForSpecialty(Character player, uint npcId)
    {
        // Sanity checks
        var backpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        if (backpack == null)
        {
            player.SendErrorMessage(ErrorMessageType.StoreBackpackNogoods);
            return 0;
        }

        var npc = WorldManager.Instance.GetNpc(npcId);
        if (npc == null)
        {
            player.SendErrorMessage(ErrorMessageType.InvalidTarget);
            return 0;
        }

        if (MathUtil.CalculateDistance(player.Transform.World.Position, npc.Transform.World.Position) > 2.5)
        {
            player.SendErrorMessage(ErrorMessageType.TooFarAway);
            return 0;
        }

        if (!_specialtyNpc.TryGetValue(npc.TemplateId, out var specialtyNpc))
        {
            player.SendErrorMessage(ErrorMessageType.StoreCantSellSameZone);
            return 1;
        }

        var bundleIdAtNPC = specialtyNpc.SpecialtyBundleId;

        if (!_specialtyBundleItemsMapped.ContainsKey(backpack.TemplateId))
        {
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return 0;
        }

        if (!_specialtyBundleItemsMapped[backpack.TemplateId].TryGetValue(bundleIdAtNPC, out var value))
        {
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return 0;
        }

        var bundleItem = value;
        if (bundleItem == null)
        {
            player.SendErrorMessage(ErrorMessageType.Invalid);
            return 0;
        }

        return (int)(Math.Floor(bundleItem.Profit * (bundleItem.Ratio / 1000f)) + bundleItem.Item.Refund);
    }

    public int SellSpecialty(Character player, uint npcObjId)
    {
        if (player.LaborPower < 60)
        {
            player.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
            return 0;
        }

        var basePrice = GetBasePriceForSpecialty(player, npcObjId);

        if (basePrice == 0) // We had an error, no need to keep going
            return basePrice;

        var priceRatio = GetRatioForSpecialty(player);

        var backpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        if (backpack == null)
        {
            player.SendErrorMessage(ErrorMessageType.StoreBackpackNogoods);
            return basePrice;
        }

        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc == null)
            return basePrice;

        // Our backpack isn't null, we have the NPC, time to calculate the profits

        // TODO: Get crafter ID of tradepack
        uint crafterId = backpack.MadeUnitId != player.Id ? backpack.MadeUnitId : 0;
        var sellerShare = 0.80f; // 80% default, set this to 1f for packs that don't share profit

        var interestRate = 5;

        var finalPriceNoInterest = (basePrice * (priceRatio / 100f));
        var interest = (finalPriceNoInterest * (interestRate / 100f));
        var amountBonus = 0; // TODO: negotiation bonus
        var finalPrice = finalPriceNoInterest + interest + amountBonus;

        var itemTypeToDeliver = npc.Template.SpecialtyCoinId;
        var amountOfItemsTotalPayout = (int)Math.Round(finalPrice);
        var amountOfItemsSeller = amountOfItemsTotalPayout;
        var amountOfItemsCrafter = 0;
        var amountOfItemsBase = basePrice;

        if (npc.Template.SpecialtyCoinId != 0)
        {
            // Items are listed in the DB at the same rate as "amounts of gold" so the value needs to be divided by 10000
            amountOfItemsTotalPayout = (int)Math.Round(amountOfItemsTotalPayout / 10000f);
            amountOfItemsSeller = (int)Math.Round(amountOfItemsSeller / 10000f);
            amountOfItemsBase = (int)Math.Round(basePrice / 10000f);
        }
        else
        {
            itemTypeToDeliver = Item.Coins;
        }

        // TODO: implement a global fsets
        var fsets = new Models.Game.Features.FeatureSet();

        // Split up the profit if needed
        if ((crafterId != 0) && (crafterId != player.Id) && fsets.Check(Models.Game.Features.Feature.backpackProfitShare))
        {
            amountOfItemsSeller = (int)Math.Round(amountOfItemsTotalPayout * sellerShare);
            amountOfItemsCrafter = amountOfItemsTotalPayout - amountOfItemsSeller;
        }

        // Mail for seller
        if (amountOfItemsSeller > 0) // This check is here for if you'd create custom packs that give 100% to crafter and 0% for delivery
        {
            var sellerMail = new MailForSpeciality(player, crafterId, backpack.TemplateId, priceRatio, itemTypeToDeliver, amountOfItemsBase, amountBonus, amountOfItemsSeller, amountOfItemsCrafter, interestRate);
            sellerMail.FinalizeForSeller();
            if (!sellerMail.Send())
            {
                player.SendErrorMessage(ErrorMessageType.MailUnknownFailure);
                return basePrice;
            }
        }

        // Mail for crafter. If seller is not crafter, send a crafter mail as well
        if ((amountOfItemsCrafter > 0) && (crafterId != 0))
        {
            var crafterMail = new MailForSpeciality(player, crafterId, backpack.TemplateId, priceRatio, itemTypeToDeliver, amountOfItemsBase, amountBonus, amountOfItemsSeller, amountOfItemsCrafter, interestRate);
            crafterMail.FinalizeForCrafter();
            if (!crafterMail.Send())
            {
                player.SendErrorMessage(ErrorMessageType.MailUnknownFailure);
                // return; // don't cancel here if we fail to send mail to crafter
            }
        }

        // Delete the backpack
        player.Inventory.Equipment.ConsumeItem(ItemTaskType.SellBackpack, backpack.TemplateId, 1, backpack);
        // TODO: Calculate proper labor by skill level
        player.ChangeLabor(-60, (int)ActabilityType.Commerce);

        // Add one pack sold in this zone during this tick
        var zoneGroupId = ZoneManager.Instance.GetZoneByKey(player.Transform.ZoneId)?.GroupId ?? 0;
        if (!_soldPackAmountInTick.ContainsKey(backpack.TemplateId))
            _soldPackAmountInTick.Add(backpack.TemplateId, new Dictionary<uint, int>());

        if (!_soldPackAmountInTick[backpack.TemplateId].ContainsKey(zoneGroupId))
            _soldPackAmountInTick[backpack.TemplateId].Add(zoneGroupId, 0);

        _soldPackAmountInTick[backpack.TemplateId][zoneGroupId] += 1;

        return basePrice;
    }

    public void ConsumeRatio()
    {
        foreach (var (itemId, zoneInfo) in _soldPackAmountInTick)
        {
            foreach (var (zoneGroupId, count) in zoneInfo)
            {
                if (count <= 0)
                    continue;

                var ratioDecrease = (int)Math.Ceiling(count * AppConfiguration.Instance.Specialty.RatioDecreasePerPack);
                InitRatioInZoneForPack(itemId, zoneGroupId);
                _soldPackAmountInTick[itemId][zoneGroupId] = 0;

                var initialRatio = _priceRatios[itemId][zoneGroupId];
                _priceRatios[itemId][zoneGroupId] = Math.Max(AppConfiguration.Instance.Specialty.MinSpecialtyRatio, initialRatio - ratioDecrease);
            }
        }
    }

    public void RegenRatio()
    {
        foreach (var soldPackItems in _soldPackAmountInTick)
        {
            foreach (var soldPacksInZone in soldPackItems.Value)
            {
                InitRatioInZoneForPack(soldPackItems.Key, soldPacksInZone.Key);
                var initialRatio = _priceRatios[soldPackItems.Key][soldPacksInZone.Key];
                _priceRatios[soldPackItems.Key][soldPacksInZone.Key] = Math.Min(
                    AppConfiguration.Instance.Specialty.MaxSpecialtyRatio,
                    initialRatio + AppConfiguration.Instance.Specialty.RatioIncreasePerTick);
            }
        }
    }

    /// <summary>
    /// Makes sure a base rate exists for the given item and zone combination
    /// </summary>
    /// <param name="itemId"></param>
    /// <param name="zoneGroupId"></param>
    private void InitRatioInZoneForPack(uint itemId, uint zoneGroupId)
    {
        if (!_priceRatios.ContainsKey(itemId))
            _priceRatios.Add(itemId, new Dictionary<uint, double>());

        if (!_priceRatios[itemId].ContainsKey(zoneGroupId))
            _priceRatios[itemId].Add(zoneGroupId, AppConfiguration.Instance.Specialty.MaxSpecialtyRatio);
    }

    // Dummy for tests
    public static int GetValueOfOne()
    {
        return 1;
    }
}
