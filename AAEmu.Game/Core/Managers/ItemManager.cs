using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Auction.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Error;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;
using Microsoft.CodeAnalysis.Text;
using MySql.Data.MySqlClient;
using NLog;
using Quartz.Util;

namespace AAEmu.Game.Core.Managers
{
    public class ItemManager : Singleton<ItemManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<int, GradeTemplate> _grades;
        private Dictionary<uint, Holdable> _holdables;
        private Dictionary<uint, Wearable> _wearables;
        private Dictionary<uint, WearableKind> _wearableKinds;
        private Dictionary<uint, WearableSlot> _wearableSlots;
        private Dictionary<uint, AttributeModifiers> _modifiers;
        private Dictionary<uint, ItemTemplate> _templates;
        private Dictionary<uint, ItemDoodadTemplate> _itemDoodadTemplates;
        private ItemConfig _config;

        // Grade Enchanting
        private Dictionary<uint, EquipSlotEnchantingCost> _enchantingCosts;
        private Dictionary<int, GradeTemplate> _gradesOrdered;
        private Dictionary<uint, ItemGradeEnchantingSupport> _enchantingSupports;

        // Gemming
        private Dictionary<uint, uint> _socketChance;
        private Dictionary<uint, List<BonusTemplate>> _itemUnitModifiers;
        private Dictionary<uint, ItemCapScale> _itemCapScales;

        // LootPacks
        private Dictionary<uint, List<LootPacks>> _lootPacks;
        private Dictionary<uint, List<LootPackDroppingNpc>> _lootPackDroppingNpc;
        private Dictionary<uint, List<LootGroups>> _lootGroups;
        private Dictionary<int, GradeDistributions> _itemGradeDistributions;
        private Dictionary<uint, List<Item>> _lootDropItems;
        
        // ItemLookConvert
        private Dictionary<uint, ItemLookConvert> _itemLookConverts;
        private Dictionary<uint, uint> _holdableItemLookConverts;
        private Dictionary<uint, uint> _wearableItemLookConverts;
        
        private Dictionary<uint, ItemProcTemplate> _itemProcTemplates;
        private Dictionary<ArmorType, Dictionary<ItemGrade, ArmorGradeBuff>> _armorGradeBuffs;
        private Dictionary<uint, EquipItemSet> _equipItemSets;
        
        // Events
        public event EventHandler OnItemsLoaded;

        private Dictionary<ulong, Item> _allItems;
        private List<ulong> _removedItems;

        public ItemTemplate GetTemplate(uint id)
        {
            return _templates.ContainsKey(id) ? _templates[id] : null;
        }

        public EquipItemSet GetEquiptItemSet(uint id)
        {
            if (_equipItemSets.TryGetValue(id, out var value))
                return value;
            else
                return null;
        }

        public GradeTemplate GetGradeTemplate(int grade)
        {
            return _grades.ContainsKey(grade) ? _grades[grade] : null;
        }

        public bool RemoveLootDropItems(uint objId)
        {
            return _lootDropItems.Remove(objId);
        }

        public Holdable GetHoldable(uint id)
        {
            return _holdables.ContainsKey(id) ? _holdables[id] : null;
        }

        public EquipSlotEnchantingCost GetEquipSlotEnchantingCost(uint slotTypeId)
        {
            return _enchantingCosts.ContainsKey(slotTypeId) ? _enchantingCosts[slotTypeId] : null;
        }

        public GradeTemplate GetGradeTemplateByOrder(int gradeOrder)
        {
            return _gradesOrdered.ContainsKey(gradeOrder) ? _gradesOrdered[gradeOrder] : null;
        }

        public ItemGradeEnchantingSupport GetItemGradEnchantingSupportByItemId(uint itemId)
        {
            return _enchantingSupports.ContainsKey(itemId) ? _enchantingSupports[itemId] : null;
        }

        public List<LootPackDroppingNpc> GetLootPackIdByNpcId(uint npcId)
        {
            return _lootPackDroppingNpc.ContainsKey(npcId) ? _lootPackDroppingNpc[npcId] : new List<LootPackDroppingNpc>();
        }

        public LootPacks[] GetLootPacks(uint lootPackId)
        {
            var res = (_lootPacks.ContainsKey(lootPackId) ? _lootPacks[lootPackId] : new List<LootPacks>()).ToArray();
            Array.Sort(res);
            return res;
        }
        public LootGroups[] GetLootGroups(uint packId)
        {
            var res = (_lootGroups.ContainsKey(packId) ? _lootGroups[packId] : new List<LootGroups>()).ToArray();
            Array.Sort(res);
            return res;
        }
        public List<Item> GetLootDropItems(uint npcId)
        {
            return _lootDropItems.ContainsKey(npcId) ? _lootDropItems[npcId] : new List<Item>();
        }
        public List<ItemTemplate> GetAllItems()
        {
            return _templates.Values.ToList();
        }
        public List<Item> CreateLootDropItems(uint npcId)
        {
            var items = GetLootDropItems(npcId);

            if (items.Count > 0)
            {
                return items;
            }
            var unit = WorldManager.Instance.GetNpc(npcId);
            if (unit == null)
            {
                return items;
            }
            var lootPackDroppingNpcs = GetLootPackIdByNpcId(unit.TemplateId);

            if (lootPackDroppingNpcs.Count <= 0)
            {
                return items;
            }
            items = new List<Item>();
            ulong itemId = ((ulong)npcId << 32) + 65536;
            foreach (var lootPackDroppingNpc in lootPackDroppingNpcs)
            {
                var lootPacks = GetLootPacks(lootPackDroppingNpc.LootPackId);
                var dropRateMax = (uint)0;
                for (var ui = 0; ui < lootPacks.Length; ui++)
                {
                    dropRateMax += lootPacks[ui].DropRate;
                }
                var dropRateItem = Rand.Next(0, dropRateMax);
                var dropRateItemId = (uint)0;
                for (var uii = 0; uii < lootPacks.Length; uii++)
                {
                    if (lootPacks[uii].DropRate + dropRateItemId >= dropRateItem)
                    {
                        Item item = new Item();
                        item.TemplateId = lootPacks[uii].ItemId;
                        item.WorldId = 1;
                        item.CreateTime = DateTime.Now;
                        item.Id = ++itemId;
                        item.MadeUnitId = npcId;
                        item.Count = Rand.Next(lootPacks[uii].MinAmount, lootPacks[uii].MaxAmount);
                        items.Add(item);
                        break;
                    }
                    else
                    {
                        dropRateItemId += lootPacks[uii].DropRate;
                    }
                }
            }
            var item2 = new Item
            {
                TemplateId = Item.Coins,
                WorldId = 1,
                CreateTime = DateTime.Now,
                Id = ++itemId,
                Count = Rand.Next(unit.Level * 5, unit.Level * 400),
                MadeUnitId = 0
                // MadeUnitId = npcId
            };
            items.Add(item2);
            _lootDropItems.Add(npcId, items);

            return items;
        }

        /// <summary>
        /// Initiate Loot item (loot all items / open loot selection window)
        /// </summary>
        /// <param name="character"></param>
        /// <param name="id"></param>
        /// <param name="lootAll"></param>
        /// <returns>True if everything was looted, false if not all could be looted</returns>
        public bool TookLootDropItems(Character character, uint id, bool lootAll)
        {
            // TODO: Bug fix for the following; 
            /*
             * Have full inventory 
             * -> Open Loot (G) 
             * -> press (F) to lootall while open (fail, bag full) 
             * -> free up bag space 
             * -> click for manual loot doesn't trigger a new packet. so it won't loot
             * Note: Re-opening the loot window lets you loot the remaining items
            */
            bool isDone = true;
            var lootDropItems = ItemManager.Instance.GetLootDropItems(id);
            if (lootAll)
            {
                for (var i = lootDropItems.Count - 1; i >= 0; --i)
                {
                    isDone &= TookLootDropItem(character, lootDropItems, lootDropItems[i], lootDropItems[i].Count);
                }
                if (lootDropItems.Count > 0)
                    character.SendPacket(new SCLootBagDataPacket(lootDropItems, lootAll));
            }
            else
            {
                isDone = (lootDropItems.Count <= 0);
                character.SendPacket(new SCLootBagDataPacket(lootDropItems, lootAll));
            }
            return isDone;
        }

        /// <summary>
        /// Takes lootDropItem from LootDropItems and adds them to character's Bag
        /// </summary>
        /// <param name="character"></param>
        /// <param name="lootDropItems"></param>
        /// <param name="lootDropItem"></param>
        /// <param name="count"></param>
        /// <returns>Returns false if the item could not be picked up.</returns>
        public bool TookLootDropItem(Character character,List<Item> lootDropItems, Item lootDropItem, int count)
        {
            var objId = (uint)(lootDropItem.Id >> 32);
            if (lootDropItem.TemplateId == Item.Coins)
            {
                character.AddMoney(SlotType.Inventory, lootDropItem.Count);
            }
            else
            {
                if (!character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Loot, lootDropItem.TemplateId,
                    count > lootDropItem.Count ? lootDropItem.Count : count, lootDropItem.Grade))
                {
                    // character.SendErrorMessage(ErrorMessageType.BagFull);
                    character.SendPacket(new SCLootItemFailedPacket(ErrorMessageType.BagFull, lootDropItem.Id, lootDropItem.TemplateId));
                    return false;
                }
            }

            lootDropItems.Remove(lootDropItem);
            character.SendPacket(new SCLootItemTookPacket(lootDropItem.TemplateId, lootDropItem.Id, lootDropItem.Count));

            if (lootDropItems.Count <= 0)
            {
                RemoveLootDropItems(objId);
                character.BroadcastPacket(new SCLootableStatePacket(objId, false), true);
            }
            return true;
        }
        public GradeDistributions GetGradeDistributions(byte id)
        {
            return _itemGradeDistributions.ContainsKey(id) ? _itemGradeDistributions[id] : null;
        }

        // note: This does "+1" because when we have 0 socketted gems, we want to get the chance for the next slot
        public uint GetSocketChance(uint numSockets)
        {
            return _socketChance.ContainsKey(numSockets + 1) ? _socketChance[numSockets + 1] : 0;
        }

        public ItemCapScale GetItemCapScale(uint skillId)
        {
            return _itemCapScales.ContainsKey(skillId) ? _itemCapScales[skillId] : null;
        }

        public float GetDurabilityRepairCostFactor()
        {
            return _config.DurabilityRepairCostFactor;
        }

        public float GetDurabilityConst()
        {
            return _config.DurabilityConst;
        }

        public float GetHoldableDurabilityConst()
        {
            return _config.HoldableDurabilityConst;
        }

        public float GetWearableDurabilityConst()
        {
            return _config.WearableDurabilityConst;
        }

        public float GetItemStatConst()
        {
            return _config.ItemStatConst;
        }

        public float GetHoldableStatConst()
        {
            return _config.HoldableStatConst;
        }

        public float GetWearableStatConst()
        {
            return _config.WearableStatConst;
        }

        public float GetStatValueConst()
        {
            return _config.StatValueConst;
        }

        public AttributeModifiers GetAttributeModifiers(uint id)
        {
            return _modifiers[id];
        }

        public List<uint> GetItemIdsFromDoodad(uint doodadID)
        {
            return _itemDoodadTemplates.ContainsKey(doodadID) ? _itemDoodadTemplates[doodadID].ItemIds : new List<uint>();
        }

        public ItemTemplate GetItemTemplateFromItemId(uint itemId)
        {
            foreach (var item in _templates)
            {
                if (item.Value.Id == itemId)
                {
                    return item.Value;
                }
            }
            return null;
        }

        public List<uint> GetItemIdsBySearchName(string searchString)
        {
            var res = new List<uint>();
            foreach (var i in _templates)
            {
                if (i.Value.searchString.Contains(searchString))
                    res.Add(i.Value.Id);
            }
            return res;
        }
        
        public ItemLookConvert GetWearableItemLookConvert(uint slotTypeId) 
        {
            if (_wearableItemLookConverts.ContainsKey(slotTypeId))
                return _itemLookConverts[_wearableItemLookConverts[slotTypeId]];
            return null;
        }

        public ItemLookConvert GetHoldableItemLookConvert(uint holdableId) 
        {
            if (_holdableItemLookConverts.ContainsKey(holdableId))
                return _itemLookConverts[_holdableItemLookConverts[holdableId]];
            return null;
        }
        
        public ItemProcTemplate GetItemProcTemplate(uint templateId)
        {
            if (_itemProcTemplates.ContainsKey(templateId))
                return _itemProcTemplates[templateId];
            return null;
        }

        public List<BonusTemplate> GetUnitModifiers(uint itemId)
        {
            if (_itemUnitModifiers.ContainsKey(itemId))
                return _itemUnitModifiers[itemId];
            return new List<BonusTemplate>();
        }

        public ArmorGradeBuff GetArmorGradeBuff(ArmorType type, ItemGrade grade)
        {
            if (!_armorGradeBuffs.ContainsKey(type))
                return null;
            if (!_armorGradeBuffs[type].ContainsKey(grade))
                return null;
            return _armorGradeBuffs[type][grade];
        }

        public Item Create(uint templateId, int count, byte grade, bool generateId = true)
        {
            var id = generateId ? ItemManager.Instance.GetNewId() : 0u;
            var template = GetTemplate(templateId);
            if (template == null)
                return null;

            Item item;
            try
            {
                item = (Item)Activator.CreateInstance(template.ClassType, id, template, count);
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                _log.Error(ex.InnerException);
                item = new Item(id, template, count);
            }

            item.Grade = grade;

            if(item.Template.BindType == ItemBindType.BindOnPickup) // Bind on pickup.
                item.SetFlag(ItemFlag.SoulBound);

            if (item.Template.FixedGrade >= 0)
                item.Grade = (byte)item.Template.FixedGrade;
            item.CreateTime = DateTime.UtcNow;
            if (generateId)
                _allItems.Add(item.Id, item);
            return item;
        }

        public void Load()
        {
            _grades = new Dictionary<int, GradeTemplate>();
            _holdables = new Dictionary<uint, Holdable>();
            _wearables = new Dictionary<uint, Wearable>();
            _wearableKinds = new Dictionary<uint, WearableKind>();
            _wearableSlots = new Dictionary<uint, WearableSlot>();
            _modifiers = new Dictionary<uint, AttributeModifiers>();
            _templates = new Dictionary<uint, ItemTemplate>();
            _enchantingCosts = new Dictionary<uint, EquipSlotEnchantingCost>();
            _gradesOrdered = new Dictionary<int, GradeTemplate>();
            _enchantingSupports = new Dictionary<uint, ItemGradeEnchantingSupport>();
            _socketChance = new Dictionary<uint, uint>();
            _itemCapScales = new Dictionary<uint, ItemCapScale>();
            _itemLookConverts = new Dictionary<uint, ItemLookConvert>();
            _holdableItemLookConverts = new Dictionary<uint, uint>();
            _wearableItemLookConverts = new Dictionary<uint, uint>();
            _lootPackDroppingNpc = new Dictionary<uint, List<LootPackDroppingNpc>>();
            _lootPacks = new Dictionary<uint, List<LootPacks>>();
            _lootGroups = new Dictionary<uint, List<LootGroups>>();
            _itemGradeDistributions = new Dictionary<int, GradeDistributions>();
            _lootDropItems = new Dictionary<uint, List<Item>>();
            _itemDoodadTemplates = new Dictionary<uint, ItemDoodadTemplate>();
            _itemProcTemplates = new Dictionary<uint, ItemProcTemplate>();
            _armorGradeBuffs = new Dictionary<ArmorType, Dictionary<ItemGrade, ArmorGradeBuff>>();
            _itemUnitModifiers = new Dictionary<uint, List<BonusTemplate>>();
            _equipItemSets = new Dictionary<uint, EquipItemSet>();
            _config = new ItemConfig();

            SkillManager.Instance.OnSkillsLoaded += OnSkillsLoaded;
            using (var connection = SQLite.CreateConnection())
            {
                _log.Info("Loading item templates ...");

                // Read configuration related to item durability and the likes
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_configs";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        if (!reader.Read())
                            return;
                        _config.DurabilityDecrementChance = reader.GetFloat("durability_decrement_chance");
                        _config.DurabilityRepairCostFactor = reader.GetFloat("durability_repair_cost_factor");
                        _config.DurabilityConst = reader.GetFloat("durability_const");
                        _config.HoldableDurabilityConst = reader.GetFloat("holdable_durability_const");
                        _config.WearableDurabilityConst = reader.GetFloat("wearable_durability_const");
                        _config.DeathDurabilityLossRatio = reader.GetInt32("death_durability_loss_ratio");
                        _config.ItemStatConst = reader.GetInt32("item_stat_const");
                        _config.HoldableStatConst = reader.GetInt32("holdable_stat_const");
                        _config.WearableStatConst = reader.GetInt32("wearable_stat_const");
                        _config.StatValueConst = reader.GetInt32("stat_value_const");
                    }
                }

                // Read Item grade related info
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_look_convert_required_items";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new ItemLookConvert();
                            template.Id = reader.GetUInt32("item_look_convert_id");
                            template.RequiredItemId = reader.GetUInt32("item_id");
                            template.RequiredItemCount = reader.GetInt32("item_count");
                            if (!_itemLookConverts.ContainsKey(template.Id))
                                _itemLookConverts.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_look_convert_holdables";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var itemLookConvertId = reader.GetUInt32("item_look_convert_id");
                            var holdableId = reader.GetUInt32("holdable_id");
                            if (!_holdableItemLookConverts.ContainsKey(holdableId))
                                _holdableItemLookConverts.Add(holdableId, itemLookConvertId);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_look_convert_wearables";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var itemLookConvertId = reader.GetUInt32("item_look_convert_id");
                            var wearableId = reader.GetUInt32("wearable_slot_id");
                            if (!_wearableItemLookConverts.ContainsKey(wearableId))
                                _wearableItemLookConverts.Add(wearableId, itemLookConvertId);
                        }
                    }
                }

                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_grades";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new GradeTemplate();
                            template.Grade = reader.GetInt32("id");
                            template.GradeOrder = reader.GetInt32("grade_order");
                            template.HoldableDps = reader.GetFloat("var_holdable_dps");
                            template.HoldableArmor = reader.GetFloat("var_holdable_armor");
                            template.HoldableMagicDps = reader.GetFloat("var_holdable_magic_dps");
                            template.WearableArmor = reader.GetFloat("var_wearable_armor");
                            template.WearableMagicResistance = reader.GetFloat("var_wearable_magic_resistance");
                            template.Durability = reader.GetFloat("durability_value");
                            template.UpgradeRatio = reader.GetInt32("upgrade_ratio");
                            template.StatMultiplier = reader.GetInt32("stat_multiplier");
                            template.RefundMultiplier = reader.GetInt32("refund_multiplier");
                            template.EnchantSuccessRatio = reader.GetInt32("grade_enchant_success_ratio");
                            template.EnchantGreatSuccessRatio = reader.GetInt32("grade_enchant_great_success_ratio");
                            template.EnchantBreakRatio = reader.GetInt32("grade_enchant_break_ratio");
                            template.EnchantDowngradeRatio = reader.GetInt32("grade_enchant_downgrade_ratio");
                            template.EnchantCost = reader.GetInt32("grade_enchant_cost");
                            template.HoldableHealDps = reader.GetFloat("var_holdable_heal_dps");
                            template.EnchantDowngradeMin = reader.GetInt32("grade_enchant_downgrade_min");
                            template.EnchantDowngradeMax = reader.GetInt32("grade_enchant_downgrade_max");
                            template.CurrencyId = reader.GetInt32("currency_id");
                            _grades.Add(template.Grade, template);
                            _gradesOrdered.Add(template.GradeOrder, template);
                        }
                    }
                }

                // Damage type related stuff for holdable weapons
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM holdables";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new Holdable
                            {
                                Id = reader.GetUInt32("id"),
                                KindId = reader.GetUInt32("kind_id"),
                                Speed = reader.GetInt32("speed"),
                                ExtraDamagePierceFactor = reader.GetInt32("extra_damage_pierce_factor"),
                                ExtraDamageSlashFactor = reader.GetInt32("extra_damage_slash_factor"),
                                ExtraDamageBluntFactor = reader.GetInt32("extra_damage_blunt_factor"),
                                MaxRange = reader.GetInt32("max_range"),
                                Angle = reader.GetInt32("angle"),
                                EnchantedDps1000 = reader.GetInt32("enchanted_dps1000"),
                                SlotTypeId = reader.GetUInt32("slot_type_id"),
                                DamageScale = reader.GetInt32("damage_scale"),
                                FormulaDps = new Formula(reader.GetString("formula_dps")),
                                FormulaMDps = new Formula(reader.GetString("formula_mdps")),
                                FormulaArmor = new Formula(reader.GetString("formula_armor")),
                                MinRange = reader.GetInt32("min_range"),
                                SheathePriority = reader.GetInt32("sheathe_priority"),
                                DurabilityRatio = reader.GetFloat("durability_ratio"),
                                RenewCategory = reader.GetInt32("renew_category"),
                                ItemProcId = reader.GetInt32("item_proc_id"),
                                StatMultiplier = reader.GetInt32("stat_multiplier"),
                                FormulaHDps = new Formula(reader.GetString("formula_hdps"))
                            };

                            _holdables.Add(template.Id, template);
                        }
                    }
                }

                // Armor rating for armor types per slot ?
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM wearables";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new Wearable
                            {
                                TypeId = reader.GetUInt32("armor_type_id"),
                                SlotTypeId = reader.GetUInt32("slot_type_id"),
                                ArmorBp = reader.GetInt32("armor_bp"),
                                MagicResistanceBp = reader.GetInt32("magic_resistance_bp")
                            };
                            _wearables.Add(template.TypeId * 128 + template.SlotTypeId, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM wearable_kinds";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new WearableKind
                            {
                                TypeId = reader.GetUInt32("armor_type_id"),
                                ArmorRatio = reader.GetInt32("armor_ratio"),
                                MagicResistanceRatio = reader.GetInt32("magic_resistance_ratio"),
                                FullBufId = reader.GetUInt32("full_buff_id"),
                                HalfBufId = reader.GetUInt32("half_buff_id"),
                                ExtraDamagePierce = reader.GetInt32("extra_damage_pierce"),
                                ExtraDamageSlash = reader.GetInt32("extra_damage_slash"),
                                ExtraDamageBlunt = reader.GetInt32("extra_damage_blunt"),
                                DurabilityRatio = reader.GetFloat("durability_ratio")
                            };
                            _wearableKinds.Add(template.TypeId, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM wearable_slots";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new WearableSlot
                            {
                                SlotTypeId = reader.GetUInt32("slot_type_id"),
                                Coverage = reader.GetInt32("coverage")
                            };
                            _wearableSlots.Add(template.SlotTypeId, template);
                        }
                    }
                }

                // Item stat bonuses (when equipped)
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM equip_item_attr_modifiers";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new AttributeModifiers
                            {
                                Id = reader.GetUInt32("id"), // TODO ... alias
                                StrWeight = reader.GetInt32("str_weight"),
                                DexWeight = reader.GetInt32("dex_weight"),
                                StaWeight = reader.GetInt32("sta_weight"),
                                IntWeight = reader.GetInt32("int_weight"),
                                SpiWeight = reader.GetInt32("spi_weight")
                            };
                            _modifiers.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_procs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new ItemProcTemplate()
                            {
                                Id = reader.GetUInt32("id"),
                                SkillId = reader.GetUInt32("skill_id"),
                                ChanceKind = (ProcChanceKind)reader.GetUInt32("chance_kind_id"),
                                ChanceRate = reader.GetUInt32("chance_rate"),
                                ChanceParam = reader.GetUInt32("chance_param"),
                                CooldownSec = reader.GetUInt32("cooldown_sec"),
                                Finisher = reader.GetBoolean("finisher", true),
                                ItemLevelBasedChanceBonus = reader.GetUInt32("item_level_based_chance_bonus"),
                            };

                            _itemProcTemplates.Add(template.Id, template);
                        }
                    }
                }


                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM equip_item_set_bonuses";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            uint id = reader.GetUInt32("equip_item_set_id");
                            if (!_equipItemSets.ContainsKey(id))
                                _equipItemSets.Add(id, new EquipItemSet { Id = id });

                            var bonus = new EquipItemSetBonus()
                            {
                                NumPieces = reader.GetInt32("num_pieces"),
                                BuffId = reader.GetUInt32("buff_id", 0),
                                ItemProcId = reader.GetUInt32("proc_id", 0)
                            };

                            if (bonus.BuffId != 0 || bonus.ItemProcId != 0)
                                _equipItemSets[id].Bonuses.Add(bonus);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_armors";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var slotTypeId = reader.GetUInt32("slot_type_id");
                            var typeId = reader.GetUInt32("type_id");

                            var template = new ArmorTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                WearableTemplate = _wearables[typeId * 128 + slotTypeId],
                                KindTemplate = _wearableKinds[typeId],
                                SlotTemplate = _wearableSlots[slotTypeId],
                                BaseEnchantable = reader.GetBoolean("base_enchantable", true),
                                ModSetId = reader.GetUInt32("mod_set_id", 0),
                                Repairable = reader.GetBoolean("repairable", true),
                                DurabilityMultiplier = reader.GetInt32("durability_multiplier"),
                                BaseEquipment = reader.GetBoolean("base_equipment", true),
                                RechargeBuffId = reader.GetUInt32("recharge_buff_id", 0),
                                ChargeLifetime = reader.GetInt32("charge_lifetime"),
                                ChargeCount = reader.GetInt32("charge_count"),
                                ItemLookConvert = GetWearableItemLookConvert(slotTypeId),
                                EquipItemSetId = reader.GetUInt32("eiset_id", 0)
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_weapons";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var holdableId = reader.GetUInt32("holdable_id");
                            var template = new WeaponTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                BaseEnchantable = reader.GetBoolean("base_enchantable", true),
                                HoldableTemplate = _holdables[holdableId],
                                ModSetId = reader.GetUInt32("mod_set_id", 0),
                                Repairable = reader.GetBoolean("repairable", true),
                                DurabilityMultiplier = reader.GetInt32("durability_multiplier"),
                                BaseEquipment = reader.GetBoolean("base_equipment", true),
                                RechargeBuffId = reader.GetUInt32("recharge_buff_id", 0),
                                ChargeLifetime = reader.GetInt32("charge_lifetime"),
                                ChargeCount = reader.GetInt32("charge_count"),
                                ItemLookConvert = GetHoldableItemLookConvert(holdableId),
                                EquipItemSetId = reader.GetUInt32("eiset_id", 0)
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_accessories";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var slotTypeId = reader.GetUInt32("slot_type_id");
                            var typeId = reader.GetUInt32("type_id");

                            var template = new AccessoryTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                WearableTemplate = _wearables[typeId * 128 + slotTypeId],
                                KindTemplate = _wearableKinds[typeId],
                                SlotTemplate = _wearableSlots[slotTypeId],
                                ModSetId = reader.GetUInt32("mod_set_id", 0),
                                Repairable = reader.GetBoolean("repairable", true),
                                DurabilityMultiplier = reader.GetInt32("durability_multiplier"),
                                RechargeBuffId = reader.GetUInt32("recharge_buff_id", 0),
                                ChargeLifetime = reader.GetInt32("charge_lifetime"),
                                ChargeCount = reader.GetInt32("charge_count"),
                                EquipItemSetId = reader.GetUInt32("eiset_id", 0)
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_summon_mates";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new SummonMateTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                NpcId = reader.GetUInt32("npc_id")
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_summon_slaves";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new SummonSlaveTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                SlaveId = reader.GetUInt32("slave_id")
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_body_parts";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            if (reader.IsDBNull("item_id"))
                                continue;
                            var template = new BodyPartTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                ModelId = reader.GetUInt32("model_id"),
                                NpcOnly = reader.GetBoolean("npc_only", true),
                                BeautyShopOnly = reader.GetBoolean("beautyshop_only", true)
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_enchanting_gems";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new RuneTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                EquipSlotGroupId = reader.GetUInt32("equip_slot_group_id", 0),
                                EquipLevel = reader.GetByte("equip_level", 0),
                                ItemGradeId = reader.GetByte("item_grade_id", 0)
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_backpacks";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new BackpackTemplate
                            {
                                Id = reader.GetUInt32("item_id"),
                                AssetId = reader.GetUInt32("asset_id"),
                                BackpackType = (BackpackType)reader.GetUInt32("backpack_type_id"),
                                DeclareSiegeZoneGroupId = reader.GetUInt32("declare_siege_zone_group_id"),
                                Heavy = reader.GetBoolean("heavy", true),
                                Asset2Id = reader.GetUInt32("asset2_id"),
                                NormalSpeciality = reader.GetBoolean("normal_specialty", true),
                                UseAsStat = reader.GetBoolean("use_as_stat", true),
                                SkinKindId = reader.GetUInt32("skin_kind_id")
                            };
                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM items";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var id = reader.GetUInt32("id");
                            var template = _templates.ContainsKey(id) ? _templates[id] : new ItemTemplate();
                            template.Id = id;
                            template.Name = reader.IsDBNull("name") ? "" : reader.GetString("name");
                            template.Category_Id = reader.GetInt32("category_id");
                            template.Level = reader.GetInt32("level");
                            template.Price = reader.GetInt32("price");
                            template.Refund = reader.GetInt32("refund");
                            template.BindType = (ItemBindType)reader.GetUInt32("bind_id");
                            template.PickupLimit = reader.GetInt32("pickup_limit");
                            template.MaxCount = reader.GetInt32("max_stack_size");
                            template.Sellable = reader.GetBoolean("sellable", true);
                            template.UseSkillId = reader.GetUInt32("use_skill_id");
                            template.UseSkillAsReagent = reader.GetBoolean("use_skill_as_reagent", true);
                            template.BuffId = reader.GetUInt32("buff_id");
                            template.Gradable = reader.GetBoolean("gradable", true);
                            template.LootMulti = reader.GetBoolean("loot_multi", true);
                            template.LootQuestId = reader.GetUInt32("loot_quest_id");
                            template.HonorPrice = reader.GetInt32("honor_price");
                            template.ExpAbsLifetime = reader.GetInt32("exp_abs_lifetime");
                            template.ExpOnlineLifetime = reader.GetInt32("exp_online_lifetime");
                            template.ExpDate = reader.IsDBNull("exp_online_lifetime") ? reader.GetInt32("exp_date") : 0;
                            template.LevelRequirement = reader.GetInt32("level_requirement");
                            template.AuctionCategoryA = reader.IsDBNull("auction_a_category_id") ? 0 : reader.GetInt32("auction_a_category_id");
                            template.AuctionCategoryB = reader.IsDBNull("auction_b_category_id") ? 0 : reader.GetInt32("auction_b_category_id");
                            template.AuctionCategoryC = reader.IsDBNull("auction_c_category_id") ? 0 : reader.GetInt32("auction_c_category_id");
                            template.LevelLimit = reader.GetInt32("level_limit");
                            template.FixedGrade = reader.GetInt32("fixed_grade");
                            template.LivingPointPrice = reader.GetInt32("living_point_price");
                            template.CharGender = reader.GetByte("char_gender_id");

                            if (!_templates.ContainsKey(template.Id))
                                _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM equip_slot_enchanting_costs";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new EquipSlotEnchantingCost();
                            template.Id = reader.GetUInt32("id");
                            template.SlotTypeId = reader.GetUInt32("slot_type_id");
                            template.Cost = reader.GetInt32("cost");
                            if (!_enchantingCosts.ContainsKey(template.SlotTypeId))
                                _enchantingCosts.Add(template.SlotTypeId, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_grade_enchanting_supports";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new ItemGradeEnchantingSupport();
                            template.Id = reader.GetUInt32("id");
                            template.ItemId = reader.GetUInt32("item_id");
                            template.RequireGradeMin = reader.GetInt32("require_grade_min");
                            template.RequireGradeMax = reader.GetInt32("require_grade_max");
                            template.AddSuccessRatio = reader.GetInt32("add_success_ratio");
                            template.AddSuccessMul = reader.GetInt32("add_success_mul");
                            template.AddGreatSuccessRatio = reader.GetInt32("add_great_success_ratio");
                            template.AddGreatSuccessMul = reader.GetInt32("add_great_success_mul");
                            template.AddBreakRatio = reader.GetInt32("add_break_ratio");
                            template.AddBreakMul = reader.GetInt32("add_break_mul");
                            template.AddDowngradeRatio = reader.GetInt32("add_downgrade_ratio");
                            template.AddDowngradeMul = reader.GetInt32("add_downgrade_mul");
                            template.AddGreatSuccessGrade = reader.GetInt32("add_great_success_grade");

                            if (!_enchantingSupports.ContainsKey(template.ItemId))
                                _enchantingSupports.Add(template.ItemId, template);
                        }
                    }
                }
                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_socket_chances";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var numSockets = reader.GetUInt32("num_sockets");
                            var chance = reader.GetUInt32("success_ratio");

                            if (!_socketChance.ContainsKey(numSockets))
                                _socketChance.Add(numSockets, chance);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_cap_scales";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new ItemCapScale();
                            template.Id = reader.GetUInt32("id");
                            template.SkillId = reader.GetUInt32("skill_id");
                            template.ScaleMin = reader.GetInt32("scale_min");
                            template.ScaleMax = reader.GetInt32("scale_max");

                            if (!_itemCapScales.ContainsKey(template.SkillId))
                                _itemCapScales.Add(template.SkillId, template);
                        }
                    }
                }


                // Load main item templates
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM loots";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new LootPacks();
                            template.Id = reader.GetUInt32("id");
                            template.Group = reader.GetInt32("group");
                            template.ItemId = reader.GetUInt32("item_id");
                            template.DropRate = reader.GetUInt32("drop_rate");
                            template.MinAmount = reader.GetInt32("min_amount");
                            template.MaxAmount = reader.GetInt32("max_amount");
                            template.LootPackId = reader.GetUInt32("loot_pack_id");
                            template.GradeId = reader.GetByte("grade_id");
                            template.AlwaysDrop = reader.GetBoolean("always_drop", true);
                            List<LootPacks> lootPacks;
                            if (_lootPacks.ContainsKey(template.LootPackId))
                                lootPacks = _lootPacks[template.LootPackId];
                            else
                            {
                                lootPacks = new List<LootPacks>();
                                _lootPacks.Add(template.LootPackId, lootPacks);
                            }

                            lootPacks.Add(template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM loot_groups";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new LootGroups();
                            template.Id = reader.GetUInt32("id");
                            template.PackId = reader.GetUInt32("pack_id");
                            template.GroupNo = reader.GetInt32("group_no");
                            template.DropRate = reader.GetUInt32("drop_rate");
                            template.ItemGradeDistributionId = reader.GetByte("item_grade_distribution_id");
                            List<LootGroups> lootGroups;
                            if (_lootGroups.ContainsKey(template.PackId))
                                lootGroups = _lootGroups[template.PackId];
                            else
                            {
                                lootGroups = new List<LootGroups>();
                                _lootGroups.Add(template.PackId, lootGroups);
                            }

                            lootGroups.Add(template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_grade_distributions";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var template = new GradeDistributions();
                            template.Id = reader.GetInt32("id");
                            template.Name = reader.GetString("name");
                            template.Weight0 = reader.GetInt32("weight_0");
                            template.Weight1 = reader.GetInt32("weight_1");
                            template.Weight2 = reader.GetInt32("weight_2");
                            template.Weight3 = reader.GetInt32("weight_3");
                            template.Weight4 = reader.GetInt32("weight_4");
                            template.Weight5 = reader.GetInt32("weight_5");
                            template.Weight6 = reader.GetInt32("weight_6");
                            template.Weight7 = reader.GetInt32("weight_7");
                            template.Weight8 = reader.GetInt32("weight_8");
                            template.Weight9 = reader.GetInt32("weight_9");
                            template.Weight10 = reader.GetInt32("weight_10");
                            template.Weight11 = reader.GetInt32("weight_11");
                            _itemGradeDistributions.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM loot_pack_dropping_npcs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new LootPackDroppingNpc();
                            template.Id = reader.GetUInt32("id");
                            template.NpcId = reader.GetUInt32("npc_id");
                            template.LootPackId = reader.GetUInt32("loot_pack_id");
                            template.DefaultPack = reader.GetBoolean("default_pack", true);
                            List<LootPackDroppingNpc> lootPackDroppingNpc;
                            if (_lootPackDroppingNpc.ContainsKey(template.NpcId))
                                lootPackDroppingNpc = _lootPackDroppingNpc[template.NpcId];
                            else
                            {
                                lootPackDroppingNpc = new List<LootPackDroppingNpc>();
                                _lootPackDroppingNpc.Add(template.NpcId, lootPackDroppingNpc);
                            }

                            lootPackDroppingNpc.Add(template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM item_spawn_doodads";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new ItemDoodadTemplate();
                            var key = reader.GetUInt32("doodad_id");
                            if (_itemDoodadTemplates.ContainsKey(key))
                            {
                                var itemId = reader.GetUInt32("item_id");
                                template = _itemDoodadTemplates[key];
                                template.ItemIds.Add(itemId);
                                _itemDoodadTemplates[key] = template;
                            }
                            else
                            {
                                template.ItemIds = new List<uint>();
                                var itemId = reader.GetUInt32("item_id");
                                template.ItemIds.Add(itemId);
                                template.DoodadId = reader.GetUInt32("doodad_id");
                                _itemDoodadTemplates.Add(template.DoodadId, template);
                            }
                        }
                    }
                }
                
                using(var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM unit_modifiers WHERE owner_type='Item'";
                    command.Prepare();
                    using(var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while(reader.Read())
                        {
                            var itemId = reader.GetUInt32("owner_id");
                            var template = new BonusTemplate
                            {
                                Attribute = (UnitAttribute)reader.GetByte("unit_attribute_id"),
                                ModifierType = (UnitModifierType)reader.GetByte("unit_modifier_type_id"),
                                Value = reader.GetInt32("value"),
                                LinearLevelBonus = reader.GetInt32("linear_level_bonus")
                            };
                            
                            if (!_itemUnitModifiers.ContainsKey(itemId))
                                _itemUnitModifiers.Add(itemId, new List<BonusTemplate>());
                            _itemUnitModifiers[itemId].Add(template);
                        }
                    }
                }
                
                using(var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM armor_grade_buffs";
                    command.Prepare();
                    using(var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while(reader.Read())
                        {
                            var armorGradeBuff = new ArmorGradeBuff()
                            {
                                Id = reader.GetByte("id"),
                                ArmorType = (ArmorType) reader.GetUInt32("armor_type_id"),
                                ItemGrade = (ItemGrade) reader.GetUInt32("item_grade_id"),
                                BuffId = reader.GetUInt32("buff_id")
                            };
                            
                            if (!_armorGradeBuffs.ContainsKey(armorGradeBuff.ArmorType))
                                _armorGradeBuffs.Add(armorGradeBuff.ArmorType, new Dictionary<ItemGrade, ArmorGradeBuff>());
                            
                            if (!_armorGradeBuffs[armorGradeBuff.ArmorType].ContainsKey(armorGradeBuff.ItemGrade))
                                _armorGradeBuffs[armorGradeBuff.ArmorType].Add(armorGradeBuff.ItemGrade, armorGradeBuff);
                        }
                    }
                }

                // Search and Translation Help Items, as well as naming missing items names (has other templates, but not in items? Removed items maybe ?)
                var invalidItemCount = 0;
                foreach(var i in _templates)
                {
                    if (i.Value.Name == null)
                    {
                        invalidItemCount++;
                        i.Value.Name = "invalid_item_" + i.Value.Id;
                    }
                    i.Value.searchString = (i.Value.Name + " " + LocalizationManager.Instance.Get("items", "name", i.Value.Id)).ToLower();
                }

                _log.Info("Loaded {0} item templates (with {1} unused) ...",_templates.Count(), invalidItemCount);


            }
            
            OnItemsLoaded?.Invoke(this, new EventArgs());
        }


        public Item GetItemByItemId(ulong itemId)
        {
            if (_allItems.TryGetValue(itemId, out var item))
                return item;
            else
                return null;
        }

        public (int, int) Save(MySqlConnection connection, MySqlTransaction transaction)
        {
            var deleteCount = 0;
            var updateCount = 0;
            // _log.Info("Saving items data ...");

            // Read configuration related to item durability and the likes
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;
                // Handle removed items in DB
                lock (_removedItems)
                {
                    if (_removedItems.Count > 0)
                    {
                        using (var deletecommand = connection.CreateCommand())
                        {
                            deletecommand.CommandText = "DELETE FROM items WHERE `id` IN(" + string.Join(",", _removedItems) + ")";
                            deletecommand.Prepare();
                            deletecommand.ExecuteNonQuery();
                        }
                        deleteCount = _removedItems.Count();
                        _removedItems.Clear();
                    }
                }
                // Update items
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                lock (_allItems)
                {
                    foreach (var entry in _allItems)
                    {
                        var item = entry.Value;
                        if (item == null)
                            continue;
                        if (item.SlotType == SlotType.None)
                        {
                            // Only give a error if it has no owner, otherwise it's likely a BuyBack item
                            if (item.OwnerId <= 0)
                                _log.Warn(string.Format("Found SlotType.None in itemslist, skipping ID:{0} - Template:{1}", item.Id, item.TemplateId));
                            continue;
                        }
                        if (!item.IsDirty)
                            continue;
                        var details = new Commons.Network.PacketStream();
                        item.WriteDetails(details);

                        command.CommandText = "REPLACE INTO items (" +
                            "`id`,`type`,`template_id`,`slot_type`,`slot`,`count`,`details`,`lifespan_mins`,`made_unit_id`," +
                            "`unsecure_time`,`unpack_time`,`owner`,`created_at`,`grade`, `flags`" +
                            ") VALUES ( " +
                            "@id, @type, @template_id, @slot_type, @slot, @count, @details, @lifespan_mins, @made_unit_id, " +
                            "@unsecure_time,@unpack_time,@owner,@created_at,@grade,@flags" +
                            ")";

                        command.Parameters.AddWithValue("@id", item.Id);
                        command.Parameters.AddWithValue("@type", item.GetType().ToString());
                        command.Parameters.AddWithValue("@template_id", item.TemplateId);
                        command.Parameters.AddWithValue("@slot_type", item.SlotType.ToString());
                        command.Parameters.AddWithValue("@slot", item.Slot);
                        command.Parameters.AddWithValue("@count", item.Count);
                        command.Parameters.AddWithValue("@details", details.GetBytes());
                        command.Parameters.AddWithValue("@lifespan_mins", item.LifespanMins);
                        command.Parameters.AddWithValue("@made_unit_id", item.MadeUnitId);
                        command.Parameters.AddWithValue("@unsecure_time", item.UnsecureTime);
                        command.Parameters.AddWithValue("@unpack_time", item.UnpackTime);
                        command.Parameters.AddWithValue("@created_at", item.CreateTime);
                        command.Parameters.AddWithValue("@owner", item.OwnerId);
                        command.Parameters.AddWithValue("@grade", item.Grade);
                        command.Parameters.AddWithValue("@flags", (byte)item.ItemFlags);
                        command.ExecuteNonQuery();
                        command.Parameters.Clear();
                        item.IsDirty = false;
                        updateCount++;
                    }
                }
            }

            return (updateCount, deleteCount);
        }



        public void LoadUserItems()
        {
            _log.Info("Loading user items ...");
            _allItems = new Dictionary<ulong, Item>();
            _removedItems = new List<ulong>();

            using (var connection = MySQL.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM items ;";

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var type = reader.GetString("type");
                        Type nClass = null;
                        try
                        {
                            nClass = Type.GetType(type);
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex);
                        }

                        if (nClass == null)
                        {
                            _log.Error("Item type {0} not found!", type);
                            continue;
                        }

                        Item item;
                        try
                        {
                            item = (Item)Activator.CreateInstance(nClass);
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex);
                            _log.Error(ex.InnerException);
                            item = new Item();
                        }

                        item.Id = reader.GetUInt64("id");
                        item.OwnerId = reader.GetUInt64("owner");
                        item.TemplateId = reader.GetUInt32("template_id");
                        item.Template = ItemManager.Instance.GetTemplate(item.TemplateId);
                        item.SlotType = (SlotType)Enum.Parse(typeof(SlotType), reader.GetString("slot_type"), true);
                        item.Slot = reader.GetInt32("slot");
                        item.Count = reader.GetInt32("count");
                        item.LifespanMins = reader.GetInt32("lifespan_mins");
                        item.MadeUnitId = reader.GetUInt32("made_unit_id");
                        item.UnsecureTime = reader.GetDateTime("unsecure_time");
                        item.UnpackTime = reader.GetDateTime("unpack_time");
                        item.CreateTime = reader.GetDateTime("created_at");
                        item.ItemFlags = (ItemFlag)reader.GetByte("flags");
                        var details = (Commons.Network.PacketStream)(byte[])reader.GetValue("details");
                        item.ReadDetails(details);

                        // Overwrite Fixed-grade items, just to make sure. Retail does not do this, but it just feels better if we do
                        if (item.Template.FixedGrade >= 0)
                            item.Grade = (byte)item.Template.FixedGrade; 
                        else if (item.Template.Gradable)
                            item.Grade = reader.GetByte("grade"); // Load from our DB if the item is gradable

                        // Add it to the global pool
                        if (!_allItems.TryAdd(item.Id, item))
                        {
                            ReleaseId(item.Id);
                            _log.Error("Failed to load item with ID {0}, possible duplicate entries!", item.Id);
                        }
                        item.IsDirty = false;

                    }
                }
            }

        }

        /// <summary>
        /// Gets a new itemID for use on new items, will also remove it from the deleted itemIDs list. Use this instead of directly calling ItemIdManager.Instance.GetNextId();
        /// </summary>
        /// <returns>A new itemID</returns>
        private ulong GetNewId()
        {
            var itemId = ItemIdManager.Instance.GetNextId();
            lock (_removedItems)
            {
                if ((itemId != 0) && _removedItems.Contains(itemId))
                    _removedItems.Remove(itemId);
            }
            return itemId;
        }

        /// <summary>
        /// Releases a itemId for re-use, will also add it to the removed items list, use instead of ItemIdManager.Instance.ReleaseId();
        /// </summary>
        /// <param name="itemId">itemId of the item to be freed up</param>
        public void ReleaseId(ulong itemId)
        {
            lock (_removedItems)
            {
                if ((itemId != 0) && !_removedItems.Contains(itemId))
                    _removedItems.Add(itemId);
            }
            lock (_allItems)
            {
                if (_allItems.ContainsKey(itemId))
                    _allItems.Remove(itemId);
            }
            // This should be the only place where ItemId ReleaseId should be called directly
            ItemIdManager.Instance.ReleaseId((uint)itemId);
        }


        public List<Item> LoadPlayerInventory(Character character)
        {
            var res = (from i in _allItems where i.Value.OwnerId == character.Id select i.Value).ToList();
            return res;
        }
        
        public void OnSkillsLoaded(object sender, EventArgs e)
        {
            foreach (var procTemplate in _itemProcTemplates.Values)
            {
                procTemplate.SkillTemplate = SkillManager.Instance.GetSkillTemplate(procTemplate.SkillId);
            }
        }

        public bool IsAutoEquipTradePack(uint itemTemplateId)
        {
            var template = GetTemplate(itemTemplateId);
            // Is a valid item, is a backpack item, doesn't bind on equip (it can bind on pickup)
            return ((template != null) && (template is BackpackTemplate bt) && !template.BindType.HasFlag(ItemBindType.BindOnEquip));
        }
    }
}
