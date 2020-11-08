using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Error;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Trading;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Specialty;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers.World
{
    public class SpecialtyManager : Singleton<SpecialtyManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public static int MAX_SPECIALTY_RATIO = 130;
        public static int MIN_SPECIALTY_RATIO = 70;
        public static float RATIO_DECREASE_PER_PACK = 0.5f;
        public static int RATIO_INCREASE_PER_TICK = 5;
        
        public static int RATIO_DECREASE_TICK_MINUTES = 1;
        public static int RATIO_REGEN_TICK_MINUTES = 60;

        private Dictionary<uint, Specialty> _specialties;
        private Dictionary<uint, SpecialtyBundleItem> _specialtyBundleItems;
        private Dictionary<uint, SpecialtyNpc> _specialtyNpc;
        
        //                 itemId           bundleId
        private Dictionary<uint, Dictionary<uint, SpecialtyBundleItem>> _specialtyBundleItemsMapped;
        //                 itemId           zoneId
        private Dictionary<uint, Dictionary<uint, int>> _priceRatios;
        //                 itemId           zoneId
        private Dictionary<uint, Dictionary<uint, int>> _soldPackAmountInTick;
        
        public void Load()
        {
            _specialties = new Dictionary<uint, Specialty>();
            _specialtyBundleItems = new Dictionary<uint, SpecialtyBundleItem>();
            _specialtyNpc = new Dictionary<uint, SpecialtyNpc>();
            _soldPackAmountInTick = new Dictionary<uint, Dictionary<uint, int>>();
            
            _specialtyBundleItemsMapped = new Dictionary<uint, Dictionary<uint, SpecialtyBundleItem>>();
            _priceRatios = new Dictionary<uint, Dictionary<uint, int>>();
            
            _log.Info("SpecialtyManager is loading...");

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
                            template.Ratio = reader.GetUInt32("ratio");
                            template.Profit = reader.GetUInt32("profit");
                            template.VendorExist = reader.GetBoolean("id", true);
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
                            
                            if (!_specialtyBundleItemsMapped.ContainsKey(template.ItemId))
                                _specialtyBundleItemsMapped.Add(template.ItemId, new Dictionary<uint, SpecialtyBundleItem>());

                            _specialtyBundleItemsMapped[template.ItemId].Add(template.SpecialtyBundleId, template);
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
                            template.Id = reader.GetUInt32("id");
                            template.Name = reader.GetString("name");
                            template.NpcId = reader.GetUInt32("npc_id");
                            template.SpecialtyBundleId = reader.GetUInt32("specialty_bundle_id");
                            
                            _specialtyNpc.Add(template.NpcId, template);
                        }
                    }
                }
            }
            
            _log.Info("SpecialtyManager loaded");
        }

        public void Initialize()
        {
            var ratioConsumeTask = new SpecialtyRatioConsumeTask();
            TaskManager.Instance.Schedule(ratioConsumeTask, TimeSpan.FromMinutes(RATIO_DECREASE_TICK_MINUTES), TimeSpan.FromMinutes(RATIO_DECREASE_TICK_MINUTES));
            
            var ratioRegenTask = new SpecialtyRatioRegenTask();
            TaskManager.Instance.Schedule(ratioRegenTask, TimeSpan.FromMinutes(RATIO_REGEN_TICK_MINUTES), TimeSpan.FromMinutes(RATIO_REGEN_TICK_MINUTES));
        }

        public void OnItemsLoaded(object sender, EventArgs e)
        {
            foreach (var specialtyBundleItem in _specialtyBundleItems.Values)
            {
                specialtyBundleItem.Item = ItemManager.Instance.GetTemplate(specialtyBundleItem.ItemId);
            }
        }

        public int GetRatioForSpecialty(Character player)
        {
            var backpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
            if (backpack == null) 
                return 0;

            var zoneId = player.Position.ZoneId;

            InitRatioInZoneForPack(backpack.TemplateId, zoneId);
           
            return _priceRatios[backpack.TemplateId][zoneId];
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

            if (MathUtil.CalculateDistance(player.Position, npc.Position) > 2.5)
            {
                player.SendErrorMessage(ErrorMessageType.TooFarAway);
                return 0;   
            }

            var bundleIdAtNPC = _specialtyNpc[npc.TemplateId].SpecialtyBundleId;

            if (!_specialtyBundleItemsMapped.ContainsKey(backpack.TemplateId))
            {
                player.SendErrorMessage(ErrorMessageType.Invalid);
                return 0;
            }
            
            if (!_specialtyBundleItemsMapped[backpack.TemplateId].ContainsKey(bundleIdAtNPC))
            {
                player.SendErrorMessage(ErrorMessageType.Invalid);
                return 0;
            }
            
            var bundleItem = _specialtyBundleItemsMapped[backpack.TemplateId][bundleIdAtNPC];
            if (bundleItem == null)
            {
                player.SendErrorMessage(ErrorMessageType.Invalid);
                return 0;
            }
            
            return (int) (Math.Floor(bundleItem.Profit * (bundleItem.Ratio / 1000f)) + bundleItem.Item.Refund);
        }

        public void SellSpecialty(Character player, uint npcObjId)
        {
            if (player.LaborPower < 60)
            {
                player.SendErrorMessage(ErrorMessageType.NotEnoughLaborPower);
                return;
            }
        
            var basePrice = GetBasePriceForSpecialty(player, npcObjId);

            if (basePrice == 0) // We had an error, no need to keep going
                return;

            var priceRatio = GetRatioForSpecialty(player);

            var backpack = player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
            if (backpack == null)
            {
                player.SendErrorMessage(ErrorMessageType.StoreBackpackNogoods);
                return;
            }

            var npc = WorldManager.Instance.GetNpc(npcObjId);
            if (npc == null)
                return;
            // Our backpack isn't null, we have the NPC, time to calculate the profits

            // TODO: Get crafter ID of tradepack
            uint crafterId = 0; // leave this at zero if seller is crafter
            var sellerShare = 0.80f; // 80% default, set this to 1f for packs that don't share profit

            var interestRate = 5;

            var finalPriceNoInterest = (basePrice * (priceRatio / 100f));
            var interest = (finalPriceNoInterest * (interestRate / 100f));
            var amountBonus = 0; // TODO: negotiation bonus
            var finalPrice = finalPriceNoInterest + interest + amountBonus ;


            var itemTypeToDeliver = npc.Template.SpecialtyCoinId;
            var amountOfItemsTotalPayout = (int)Math.Round(finalPrice);
            var amountOfItemsSeller = amountOfItemsTotalPayout ;
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
                    return;
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
            if (!_soldPackAmountInTick.ContainsKey(backpack.TemplateId))
                _soldPackAmountInTick.Add(backpack.TemplateId, new Dictionary<uint, int>());
            
            if (!_soldPackAmountInTick[backpack.TemplateId].ContainsKey(player.Position.ZoneId))
                _soldPackAmountInTick[backpack.TemplateId].Add(player.Position.ZoneId, 0);

            _soldPackAmountInTick[backpack.TemplateId][player.Position.ZoneId] += 1;
        }

        public void ConsumeRatio()
        {
            foreach (var soldPackItems in _soldPackAmountInTick)
            {
                foreach (var soldPacksInZone in soldPackItems.Value)
                {
                    var ratioDecrease = (int) Math.Ceiling(soldPacksInZone.Value * RATIO_DECREASE_PER_PACK);
                    InitRatioInZoneForPack(soldPackItems.Key, soldPacksInZone.Key);

                    var initialRatio = _priceRatios[soldPackItems.Key][soldPacksInZone.Key];
                    _priceRatios[soldPackItems.Key][soldPacksInZone.Key] = Math.Max(MIN_SPECIALTY_RATIO, initialRatio - ratioDecrease);
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
                    _priceRatios[soldPackItems.Key][soldPacksInZone.Key] = Math.Min(MAX_SPECIALTY_RATIO, initialRatio + RATIO_INCREASE_PER_TICK);
                }
            }
        }

        private void InitRatioInZoneForPack(uint itemId, uint zoneId)
        {
            if (!_priceRatios.ContainsKey(itemId)) 
                _priceRatios.Add(itemId, new Dictionary<uint, int>());
            
            if (!_priceRatios[itemId].ContainsKey(zoneId))
                _priceRatios[itemId].Add(zoneId, MAX_SPECIALTY_RATIO);
        }

        // Dummy for tests
        public int GetValueOfOne()
        {
            return 1;
        }
    }
}
