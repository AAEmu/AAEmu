using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Auction.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Loots;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Items.Slave;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Item;
using AAEmu.Game.Utils.DB;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class ItemManager : Singleton<ItemManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded;

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

    // Socketing
    private Dictionary<uint, uint> _socketChance;
    private Dictionary<uint, List<BonusTemplate>> _itemUnitModifiers;
    private Dictionary<uint, ItemCapScale> _itemCapScales;

    // Loot related
    private Dictionary<uint, List<LootPackDroppingNpc>> _lootPackDroppingNpc;
    private Dictionary<uint, List<LootPackConvertFish>> _lootPackConvertFish;
    private Dictionary<int, GradeDistributions> _itemGradeDistributions;
    private Dictionary<uint, List<Item>> _lootDropItems;

    // ItemLookConvert
    private Dictionary<uint, ItemLookConvert> _itemLookConverts;
    private Dictionary<uint, uint> _holdableItemLookConverts;
    private Dictionary<uint, uint> _wearableItemLookConverts;

    private Dictionary<uint, ItemProcTemplate> _itemProcTemplates;
    private Dictionary<ArmorType, Dictionary<ItemGrade, ArmorGradeBuff>> _armorGradeBuffs;
    private Dictionary<uint, EquipItemSet> _equipItemSets;
    private Dictionary<uint, uint> _defaultDyeIds;
    private Dictionary<uint, ItemSet> _itemSets;

    // ItemSlaveEquip
    private Dictionary<uint, ItemSlaveEquip> _ItemSlaveEquips;

    // Events
    public event EventHandler OnItemsLoaded;
    private DateTime LastTimerCheck { get; set; }
    private object ItemTimerLock { get; set; }

    private Dictionary<ulong, Item> _allItems;
    private List<ulong> _removedItems;
    private Dictionary<ulong, ItemContainer> _allPersistentContainers;
    private bool _loadedUserItems;
    // private Dictionary<ulong, Item> _timerSubscriptionsItems;

    public ItemTemplate GetTemplate(uint id)
    {
        return _templates.GetValueOrDefault(id);
    }

    public EquipItemSet GetEquippedItemSet(uint id)
    {
        return _equipItemSets.GetValueOrDefault(id);
    }

    public GradeTemplate GetGradeTemplate(int grade)
    {
        return _grades.GetValueOrDefault(grade);
    }

    public bool RemoveLootDropItems(uint objId)
    {
        return _lootDropItems.Remove(objId);
    }

    public Holdable GetHoldable(uint id)
    {
        return _holdables.GetValueOrDefault(id);
    }

    public EquipSlotEnchantingCost GetEquipSlotEnchantingCost(uint slotTypeId)
    {
        return _enchantingCosts.GetValueOrDefault(slotTypeId);
    }

    public GradeTemplate GetGradeTemplateByOrder(int gradeOrder)
    {
        return _gradesOrdered.GetValueOrDefault(gradeOrder);
    }

    public ItemGradeEnchantingSupport GetItemGradEnchantingSupportByItemId(uint itemId)
    {
        return _enchantingSupports.GetValueOrDefault(itemId);
    }

    private List<LootPackDroppingNpc> GetLootPackIdByNpcId(uint npcId)
    {
        return _lootPackDroppingNpc.TryGetValue(npcId, out var value) ? value : [];
    }

    /// <summary>
    /// GetLootPackIdByItemId - designed to transform fish into trophies
    /// </summary>
    /// <param name="itemId"></param>
    /// <returns></returns>
    private List<LootPackConvertFish> GetLootPackIdByItemId(uint itemId)
    {
        return _lootPackConvertFish.TryGetValue(itemId, out var value) ? value : [];
    }

    public List<Item> GetLootDropItems(uint npcId)
    {
        return _lootDropItems.TryGetValue(npcId, out var item) ? item : [];
    }

    public List<ItemTemplate> GetAllItems()
    {
        return _templates.Values.ToList();
    }

    public List<Item> CreateLootDropItems(uint npcId, BaseUnit killer)
    {
        var items = GetLootDropItems(npcId);

        // Already generated?
        if (items.Count > 0)
        {
            return items;
        }

        // Check if NPC actually exists
        var unit = WorldManager.Instance.GetNpc(npcId);
        if (unit == null)
        {
            return items;
        }

        // Get drop lists
        var lootPackDroppingNpcs = GetLootPackIdByNpcId(unit.TemplateId);
        if (lootPackDroppingNpcs.Count <= 0)
        {
            return items;
        }

        // Calculate loot rates
        var lootDropRate = 1f;
        var lootGoldRate = 1f;

        // Check all people with a claim on the NPC

        var eligiblePlayers = new HashSet<Character>();
        if (unit.CharacterTagging.TagTeam != 0)
        {
            //A team has tagging rights
            var team = TeamManager.Instance.GetActiveTeam(unit.CharacterTagging.TagTeam);
            if (team != null)
            {
                foreach (var member in team.Members)
                {
                    if (member == null || member.Character == null)
                        continue;

                    var distance = member.Character.Transform.World.Position - unit.Transform.World.Position;
                    if (distance.Length() <= 200)
                    {
                        //This player is in range of the mob and in a group with tagging rights.
                        eligiblePlayers.Add(member.Character);
                    }
                }
            }
            else if (unit.CharacterTagging.Tagger != null)
            {
                //A player has tag rights
                eligiblePlayers.Add(unit.CharacterTagging.Tagger);
            }

        }
        else if (unit.CharacterTagging.Tagger != null)
        {
            //A player has tag rights
            eligiblePlayers.Add(unit.CharacterTagging.Tagger);
        }
        if (eligiblePlayers.Count > 0)
        {
            var maxDropRateMul = -100f;
            var maxLootGoldMul = -100f;

            foreach (var pl in eligiblePlayers)
            {

                var aggroDropMul = (100f + pl.DropRateMul) / 100f;
                var aggroGoldMul = (100f + pl.LootGoldMul) / 100f;
                if (aggroDropMul > maxDropRateMul)
                    maxDropRateMul = aggroDropMul;
                if (aggroGoldMul > maxLootGoldMul)
                    maxLootGoldMul = aggroGoldMul;



            }

            lootDropRate = maxDropRateMul;
            lootGoldRate = maxLootGoldMul;



        }
        else if (killer is Character player)
        {
            lootDropRate *= (100f + player.DropRateMul) / 100f;
            lootGoldRate *= (100f + player.LootGoldMul) / 100f;
            Logger.Info($"Unit killed without aggro: {unit.ObjId} ({unit.TemplateId}) by {player.Name}");
        }

        // Base ID used for identifying the loot
        var baseId = ((ulong)unit.ObjId << 32) + 65536;

        // Generate the actual loot
        foreach (var lootPackDropping in lootPackDroppingNpcs)
        {
            var lootPack = LootGameData.Instance.GetPack(lootPackDropping.LootPackId);
            if (lootPack == null)
                continue;
            items = lootPack.GenerateNpcPackItems(ref baseId, lootDropRate, lootGoldRate);
            if (!_lootDropItems.TryAdd(npcId, items))
                _lootDropItems[npcId].AddRange(items);
        }

        if (!_lootDropItems.TryGetValue(npcId, out items))
            items = new List<Item>();
        return items;
    }

    public List<Item> GetLootConvertFish(uint templateId)
    {
        var items = new List<Item>();
        var lootPackConvertFishes = GetLootPackIdByItemId(templateId);

        if (lootPackConvertFishes.Count <= 0)
        {
            return items;
        }

        foreach (var lootPackConvertFish in lootPackConvertFishes)
        {
            var lootPacks = LootGameData.Instance.GetPack(lootPackConvertFish.LootPackId);
            var dropRateMax = (uint)0;
            for (var ui = 0; ui < lootPacks.Loots?.Count; ui++)
            {
                dropRateMax += lootPacks.Loots[ui].DropRate;
            }
            var dropRateItem = Rand.Next(0, dropRateMax);
            var dropRateItemId = 0u;
            for (var uii = 0; uii < (lootPacks.Loots?.Count ?? 0); uii++)
            {
                if (lootPacks.Loots?[uii].DropRate + dropRateItemId >= dropRateItem)
                {
                    var item = new Item();
                    item.TemplateId = lootPacks.Loots[uii].ItemId;
                    item.CreateTime = DateTime.UtcNow;
                    item.Id = Instance.GetNewId();
                    item.MadeUnitId = templateId;
                    item.Count = Rand.Next(lootPacks.Loots[uii].MinAmount, lootPacks.Loots[uii].MaxAmount);
                    items.Add(item);
                    break;
                }

                if (lootPacks.Loots != null)
                {
                    dropRateItemId += lootPacks.Loots[uii].DropRate;
                }
            }
            break; // TODO use only the first item
        }

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
         * -> press (F) to loot all while open (fail, bag full) 
         * -> free up bag space 
         * -> click for manual loot doesn't trigger a new packet. so it won't loot
         * Note: Re-opening the loot window lets you loot the remaining items
        */
        var isDone = true;
        var lootDropItems = Instance.GetLootDropItems(id);
        if (lootAll)
        {
            for (var i = lootDropItems.Count - 1; i >= 0; --i)
            {
                isDone &= TookLootDropItem(character, lootDropItems, lootDropItems[i], lootDropItems[i].Count);
            }
            if (lootDropItems.Count > 0)
                character.SendPacket(new SCLootingBagPacket(lootDropItems, true));
        }
        else
        {
            isDone = lootDropItems.Count <= 0;
            character.SendPacket(new SCLootingBagPacket(lootDropItems, false));
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
    public bool TookLootDropItem(Character character, List<Item> lootDropItems, Item lootDropItem, int count)
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
        return _itemGradeDistributions.GetValueOrDefault(id);
    }

    // note: This does "+1" because when we have 0 socket-ed gems, we want to get the chance for the next slot
    public uint GetSocketChance(uint numSockets)
    {
        return _socketChance.ContainsKey(numSockets + 1) ? _socketChance[numSockets + 1] : 0;
    }

    public ItemCapScale GetItemCapScale(uint skillId)
    {
        return _itemCapScales.GetValueOrDefault(skillId);
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

    public List<uint> GetItemIdsFromDoodad(uint doodadId)
    {
        return _itemDoodadTemplates.TryGetValue(doodadId, out var template) ? template.ItemIds : new List<uint>();
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

    private List<uint> GetItemIdsBySearchName(string searchString)
    {
        var res = new List<uint>();
        foreach (var i in _templates)
        {
            if (i.Value.searchString.Contains(searchString))
                res.Add(i.Value.Id);
        }
        return res;
    }

    public List<ItemTemplate> GetItemTemplatesForAuctionSearch(AuctionSearchTemplate searchTemplate)
    {
        var templateList = new List<ItemTemplate>();
        var itemIds = new List<uint>();

        if (searchTemplate.ItemName != "")
            itemIds = GetItemIdsBySearchName(searchTemplate.ItemName);

        if (itemIds.Count > 0)
        {
            for (var i = 0; i < itemIds.Count; i++)
            {
                var i1 = i;
                var query = from item in _templates.Values
                            where itemIds[i1] == 0 || item.Id == itemIds[i1]
                            where searchTemplate.CategoryA == 0 || item.AuctionCategoryA == searchTemplate.CategoryA
                            where searchTemplate.CategoryB == 0 || item.AuctionCategoryB == searchTemplate.CategoryB
                            where searchTemplate.CategoryC == 0 || item.AuctionCategoryC == searchTemplate.CategoryC
                            select item;
                var itemList = query.ToList();

                foreach (var item in itemList)
                {
                    templateList.Add(item);
                }
            }
            return templateList;
        }
        else
        {
            var query = from item in _templates.Values
                        where searchTemplate.CategoryA == 0 || item.AuctionCategoryA == searchTemplate.CategoryA
                        where searchTemplate.CategoryB == 0 || item.AuctionCategoryB == searchTemplate.CategoryB
                        where searchTemplate.CategoryC == 0 || item.AuctionCategoryC == searchTemplate.CategoryC
                        select item;
            templateList = query.ToList();
            return templateList;
        }
    }

    private ItemLookConvert GetWearableItemLookConvert(uint slotTypeId)
    {
        if (_wearableItemLookConverts.TryGetValue(slotTypeId, out var convert))
            return _itemLookConverts[convert];
        return null;
    }

    private ItemLookConvert GetHoldableItemLookConvert(uint holdableId)
    {
        if (_holdableItemLookConverts.TryGetValue(holdableId, out var convert))
            return _itemLookConverts[convert];
        return null;
    }

    private uint GetDyeableItemDefaultDyeId(uint itemId)
    {
        if (_defaultDyeIds.TryGetValue(itemId, out var dyeItemId))
            return dyeItemId;
        return 0;
    }

    public ItemProcTemplate GetItemProcTemplate(uint templateId)
    {
        return _itemProcTemplates.GetValueOrDefault(templateId);
    }

    public List<BonusTemplate> GetUnitModifiers(uint itemId)
    {
        if (_itemUnitModifiers.TryGetValue(itemId, out var modifiers))
            return modifiers;
        return new List<BonusTemplate>();
    }

    public ArmorGradeBuff GetArmorGradeBuff(ArmorType type, ItemGrade grade)
    {
        return !_armorGradeBuffs.TryGetValue(type, out var value) ? null : value.GetValueOrDefault(grade);
    }

    public Item Create(uint templateId, int count, byte grade, bool generateId = true)
    {
        var id = generateId ? Instance.GetNewId() : 0u;
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
            Logger.Error(ex);
            Logger.Error(ex.InnerException);
            item = new Item(id, template, count);
        }

        if (item == null)
            return null;

        item.Grade = grade;

        if (item.Template.BindType == ItemBindType.BindOnPickup) // Bind on pickup.
            item.SetFlag(ItemFlag.SoulBound);

        if (item.Template.FixedGrade >= 0)
            item.Grade = (byte)item.Template.FixedGrade;
        item.CreateTime = DateTime.UtcNow;
        if (generateId)
        {
            if (!_allItems.TryAdd(item.Id, item))
            {
                Logger.Error($"Failed to load item with ID {item.Id}, possible duplicate entries!");
                return null;
            }
        }

        return item;
    }

    public bool AddItem(Item item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (!_allItems.TryAdd(item.Id, item))
        {
            Logger.Error($"Failed to load item with ID {item.Id}, possible duplicate entries!");
            return false;
        }
        return true;
    }

    public void Load()
    {
        if (_loaded)
            return;

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
        _lootPackConvertFish = new Dictionary<uint, List<LootPackConvertFish>>();
        _itemGradeDistributions = new Dictionary<int, GradeDistributions>();
        /*
        _lootPacks = new Dictionary<uint, List<Loot>>();
        _lootGroups = new Dictionary<uint, List<LootGroups>>();
        */
        _lootDropItems = new Dictionary<uint, List<Item>>();
        _itemDoodadTemplates = new Dictionary<uint, ItemDoodadTemplate>();
        _itemProcTemplates = new Dictionary<uint, ItemProcTemplate>();
        _armorGradeBuffs = new Dictionary<ArmorType, Dictionary<ItemGrade, ArmorGradeBuff>>();
        _itemUnitModifiers = new Dictionary<uint, List<BonusTemplate>>();
        _equipItemSets = new Dictionary<uint, EquipItemSet>();
        _defaultDyeIds = new Dictionary<uint, uint>();
        _itemSets = new Dictionary<uint, ItemSet>();
        _config = new ItemConfig();
        ItemTimerLock = new();
        LastTimerCheck = DateTime.UtcNow;
        _ItemSlaveEquips = new Dictionary<uint, ItemSlaveEquip>();

        SkillManager.Instance.OnSkillsLoaded += OnSkillsLoaded;

        using (var connection2 = SQLite.CreateConnection("Data", "compact.server.table.sqlite3"))
        using (var connection = SQLite.CreateConnection())
        {
            Logger.Info("Loading item templates ...");

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
                        var template = new ItemLookConvert
                        {
                            Id = reader.GetUInt32("item_look_convert_id"),
                            RequiredItemId = reader.GetUInt32("item_id"),
                            RequiredItemCount = reader.GetInt32("item_count")
                        };
                        _itemLookConverts.TryAdd(template.Id, template);
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
                        _holdableItemLookConverts.TryAdd(holdableId, itemLookConvertId);
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
                        _wearableItemLookConverts.TryAdd(wearableId, itemLookConvertId);
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
                        var template = new GradeTemplate
                        {
                            Grade = reader.GetInt32("id"),
                            GradeOrder = reader.GetInt32("grade_order"),
                            HoldableDps = reader.GetFloat("var_holdable_dps"),
                            HoldableArmor = reader.GetFloat("var_holdable_armor"),
                            HoldableMagicDps = reader.GetFloat("var_holdable_magic_dps"),
                            WearableArmor = reader.GetFloat("var_wearable_armor"),
                            WearableMagicResistance = reader.GetFloat("var_wearable_magic_resistance"),
                            Durability = reader.GetFloat("durability_value"),
                            UpgradeRatio = reader.GetInt32("upgrade_ratio"),
                            StatMultiplier = reader.GetInt32("stat_multiplier"),
                            RefundMultiplier = reader.GetInt32("refund_multiplier"),
                            //template.EnchantSuccessRatio = reader.GetInt32("grade_enchant_success_ratio"); // there is no such field in the database for version 3.0.3.0
                            //template.EnchantGreatSuccessRatio = reader.GetInt32("grade_enchant_great_success_ratio"); // there is no such field in the database for version 3.0.3.0
                            //template.EnchantBreakRatio = reader.GetInt32("grade_enchant_break_ratio"); // there is no such field in the database for version 3.0.3.0
                            //template.EnchantDowngradeRatio = reader.GetInt32("grade_enchant_downgrade_ratio"); // there is no such field in the database for version 3.0.3.0
                            //template.EnchantCost = reader.GetInt32("grade_enchant_cost"); // there is no such field in the database for version 3.0.3.0
                            //template.HoldableHealDps = reader.GetFloat("var_holdable_heal_dps"); // there is no such field in the database for version 3.0.3.0
                            //template.EnchantDowngradeMin = reader.GetInt32("grade_enchant_downgrade_min"); // there is no such field in the database for version 3.0.3.0
                            //template.EnchantDowngradeMax = reader.GetInt32("grade_enchant_downgrade_max"); // there is no such field in the database for version 3.0.3.0
                            //template.CurrencyId = reader.GetInt32("currency_id"); // there is no such field in the database for version 3.0.3.0
                        };
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
                            //KindId = reader.GetUInt32("kind_id"); // there is no such field in the database for version 3.0.3.0
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
                            //MagicResistanceBp = reader.GetInt32("magic_resistance_bp") // there is no such field in the database for version 3.0.3.0
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
                            //template.ArmorRatio = reader.GetInt32("armor_ratio"); // there is no such field in the database for version 3.0.3.0
                            //template.MagicResistanceRatio = reader.GetInt32("magic_resistance_ratio"); // there is no such field in the database for version 3.0.3.0
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
            /*
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM dyeable_items";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        _defaultDyeIds.Add(reader.GetUInt32("item_id"), reader.GetUInt32("default_dyeing_item_id"));
                    }
                }
            }
            */
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
                        var id = reader.GetUInt32("equip_item_set_id");
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

                        var template = new ArmorTemplate();
                        template.Id = reader.GetUInt32("item_id");
                        template.WearableTemplate = _wearables[typeId * 128 + slotTypeId];
                        template.KindTemplate = _wearableKinds[typeId];
                        template.SlotTemplate = _wearableSlots[slotTypeId];
                        template.BaseEnchantable = reader.GetBoolean("base_enchantable", true);
                        template.ModSetId = reader.GetUInt32("mod_set_id", 0);
                        template.Repairable = reader.GetBoolean("repairable", true);
                        template.DurabilityMultiplier = reader.GetInt32("durability_multiplier");
                        template.BaseEquipment = reader.GetBoolean("base_equipment", true);
                        template.RechargeBuffId = reader.GetUInt32("recharge_buff_id", 0);
                        template.ChargeLifetime = reader.GetInt32("charge_lifetime", 0);
                        template.ChargeCount = reader.GetInt16("charge_count");
                        template.ItemLookConvert = GetWearableItemLookConvert(slotTypeId);
                        template.EquipItemSetId = reader.GetUInt32("eiset_id", 0);
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
                            BaseEnchantable = reader.GetBoolean("base_enchantable"),
                            HoldableTemplate = _holdables[holdableId],
                            ModSetId = reader.GetUInt32("mod_set_id", 0),
                            Repairable = reader.GetBoolean("repairable", true),
                            DurabilityMultiplier = reader.GetInt32("durability_multiplier"),
                            BaseEquipment = reader.GetBoolean("base_equipment", true),
                            RechargeBuffId = reader.GetUInt32("recharge_buff_id", 0),
                            ChargeLifetime = reader.GetInt32("charge_lifetime", 0),
                            ChargeCount = reader.GetInt16("charge_count"),
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
                            ChargeLifetime = reader.GetInt32("charge_lifetime", 0),
                            ChargeCount = reader.GetInt16("charge_count"),
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
                            ItemId = reader.GetUInt32("item_id"),
                            ModelId = reader.GetUInt32("model_id"),
                            NpcOnly = reader.GetBoolean("npc_only", true),
                            SlotTypeId = reader.GetUInt32("slot_type_id"),
                            //BeautyShopOnly = reader.GetBoolean("beautyshop_only", true) // there is no in the database for version 3.0.3.0
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
                        var template = new BackpackTemplate();
                        template.Id = reader.GetUInt32("item_id");
                        template.AssetId = reader.GetUInt32("asset_id");
                        template.Asset2Id = reader.GetUInt32("asset2_id");
                        template.BackpackType = (BackpackType)reader.GetUInt32("backpack_type_id");
                        template.DeclareSiegeZoneGroupId = reader.GetUInt32("declare_siege_zone_group_id");
                        template.FreshnessGroupId = reader.GetUInt32("freshness_group_id");
                        template.GliderAnimActionId = reader.GetUInt32("glider_anim_action_id");
                        template.GliderFastAnimActionId = reader.GetUInt32("glider_fast_anim_action_id");
                        template.GliderSlidingAnimActionId = reader.GetUInt32("glider_sliding_anim_action_id");
                        template.GliderSlowAnimActionId = reader.GetUInt32("glider_slow_anim_action_id");
                        //template.NormalSpeciality = reader.GetBoolean("normal_specialty");
                        template.Heavy = reader.GetBoolean("heavy");
                        template.SkinKindId = reader.GetUInt32("skin_kind_id");
                        template.UseAsStat = reader.GetBoolean("use_as_stat");

                        _templates.Add(template.Id, template);
                    }
                }
            }

            // TODO: HACK-FIX FOR CREST INK/STAMP/MUSIC
            var crestInkItemTemplate = new UccTemplate { Id = Item.CrestInk };
            _templates.Add(crestInkItemTemplate.Id, crestInkItemTemplate);

            var crestStampItemTemplate = new UccTemplate { Id = Item.CrestStamp };
            _templates.Add(crestStampItemTemplate.Id, crestStampItemTemplate);

            var sheetMusicItemTemplate = new MusicSheetTemplate { Id = Item.SheetMusic };
            _templates.Add(sheetMusicItemTemplate.Id, sheetMusicItemTemplate);

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
                        var template = _templates.TryGetValue(id, out var templateRes) ? templateRes : new ItemTemplate();
                        template.Id = id;
                        template.Name = reader.IsDBNull("name") ? "" : reader.GetString("name");
                        template.CategoryId = reader.GetInt32("category_id");
                        template.Level = reader.GetInt32("level");
                        template.Price = reader.GetInt32("price");
                        template.Refund = reader.GetInt32("refund");
                        template.BindType = (ItemBindType)reader.GetUInt32("bind_id");
                        template.PickupLimit = reader.GetInt32("pickup_limit");
                        template.MaxCount = reader.GetInt32("max_stack_size");
                        template.Sellable = reader.GetBoolean("sellable", true);
                        template.UseSkillId = reader.GetUInt32("use_skill_id");
                        template.UseSkillAsReagent = reader.GetBoolean("use_skill_as_reagent", true);
                        template.ImplId = (ItemImplEnum)reader.GetInt32("impl_id");
                        template.BuffId = reader.GetUInt32("buff_id");
                        template.Gradable = reader.GetBoolean("gradable", true);
                        template.LootMulti = reader.GetBoolean("loot_multi", true);
                        template.LootQuestId = reader.GetUInt32("loot_quest_id");
                        template.HonorPrice = reader.GetInt32("honor_price");
                        template.ExpAbsLifetime = reader.GetInt32("exp_abs_lifetime");
                        template.ExpOnlineLifetime = reader.GetInt32("exp_online_lifetime");
                        template.ExpDate = reader.IsDBNull("exp_date") ? reader.GetInt32("exp_date") : 0;
                        template.SpecialtyZoneId = !reader.IsDBNull("specialty_zone_id") ? reader.GetUInt32("specialty_zone_id") : 0;
                        template.LevelRequirement = reader.GetInt32("level_requirement");
                        template.AuctionCategoryA = reader.IsDBNull("auction_a_category_id") ? 0 : reader.GetInt32("auction_a_category_id");
                        template.AuctionCategoryB = reader.IsDBNull("auction_b_category_id") ? 0 : reader.GetInt32("auction_b_category_id");
                        template.AuctionCategoryC = reader.IsDBNull("auction_c_category_id") ? 0 : reader.GetInt32("auction_c_category_id");
                        template.LevelLimit = reader.GetInt32("level_limit");
                        template.FixedGrade = reader.GetInt32("fixed_grade");
                        template.Disenchantable = reader.GetBoolean("disenchantable", true);
                        template.LivingPointPrice = reader.GetInt32("living_point_price");
                        template.CharGender = reader.GetByte("char_gender_id");

                        _templates.TryAdd(template.Id, template);
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
                        var template = new EquipSlotEnchantingCost
                        {
                            //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                            SlotTypeId = reader.GetUInt32("slot_type_id"),
                            Cost = reader.GetInt32("cost")
                        };
                        _enchantingCosts.TryAdd(template.SlotTypeId, template);
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
                        var template = new ItemGradeEnchantingSupport
                        {
                            //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                            ItemId = reader.GetUInt32("item_id"),
                            RequireGradeMin = reader.GetInt32("require_grade_min"),
                            RequireGradeMax = reader.GetInt32("require_grade_max"),
                            AddSuccessRatio = reader.GetInt32("add_success_ratio"),
                            AddSuccessMul = reader.GetInt32("add_success_mul"),
                            AddGreatSuccessRatio = reader.GetInt32("add_great_success_ratio"),
                            AddGreatSuccessMul = reader.GetInt32("add_great_success_mul"),
                            AddBreakRatio = reader.GetInt32("add_break_ratio"),
                            AddBreakMul = reader.GetInt32("add_break_mul"),
                            AddDowngradeRatio = reader.GetInt32("add_downgrade_ratio"),
                            AddDowngradeMul = reader.GetInt32("add_downgrade_mul"),
                            AddGreatSuccessGrade = reader.GetInt32("add_great_success_grade")
                        };

                        _enchantingSupports.TryAdd(template.ItemId, template);
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
                        var numSockets = reader.GetUInt32("id"); // num_sockets
                        var chance = reader.GetUInt32("cost_ratio"); // success_ratio

                        _socketChance.TryAdd(numSockets, chance);
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
                        var template = new ItemCapScale
                        {
                            //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                            SkillId = reader.GetUInt32("skill_id"),
                            ScaleMin = reader.GetInt32("scale_min"),
                            ScaleMax = reader.GetInt32("scale_max")
                        };

                        _itemCapScales.TryAdd(template.SkillId, template);
                    }
                }
            }

            // Load main item templates

            /*
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM loots";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new Loot();
                        template.Id = reader.GetUInt32("id");
                        template.Group = reader.GetUInt32("group");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.DropRate = reader.GetUInt32("drop_rate");
                        template.MinAmount = reader.GetInt32("min_amount");
                        template.MaxAmount = reader.GetInt32("max_amount");
                        template.LootPackId = reader.GetUInt32("loot_pack_id");
                        template.GradeId = reader.GetByte("grade_id");
                        template.AlwaysDrop = reader.GetBoolean("always_drop");
                        List<Loot> lootPacks;
                        if (_lootPacks.ContainsKey(template.LootPackId))
                            lootPacks = _lootPacks[template.LootPackId];
                        else
                        {
                            lootPacks = new List<Loot>();
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
                        template.GroupNo = reader.GetUInt32("group_no");
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
                    */

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM item_grade_distributions";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var template = new GradeDistributions
                        {
                            Id = reader.GetInt32("id"),
                            //template.Name = reader.GetString("name"); // there is no such field in the database for version 3.0.3.0
                            Weight0 = reader.GetInt32("weight_0"),
                            Weight1 = reader.GetInt32("weight_1"),
                            Weight2 = reader.GetInt32("weight_2"),
                            Weight3 = reader.GetInt32("weight_3"),
                            Weight4 = reader.GetInt32("weight_4"),
                            Weight5 = reader.GetInt32("weight_5"),
                            Weight6 = reader.GetInt32("weight_6"),
                            Weight7 = reader.GetInt32("weight_7"),
                            Weight8 = reader.GetInt32("weight_8"),
                            Weight9 = reader.GetInt32("weight_9"),
                            Weight10 = reader.GetInt32("weight_10"),
                            Weight11 = reader.GetInt32("weight_11")
                        };
                        _itemGradeDistributions.Add(template.Id, template);
                    }
                }
            }

            using (var command = connection2.CreateCommand())
            {
                command.CommandText = "SELECT * FROM loot_pack_dropping_npcs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new LootPackDroppingNpc
                        {
                            Id = reader.GetUInt32("id"),
                            NpcId = reader.GetUInt32("npc_id"),
                            LootPackId = reader.GetUInt32("loot_pack_id"),
                            DefaultPack = reader.GetBoolean("default_pack")
                        };
                        List<LootPackDroppingNpc> lootPackDroppingNpc;
                        if (_lootPackDroppingNpc.TryGetValue(template.NpcId, out var value))
                            lootPackDroppingNpc = value;
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
                command.CommandText = "SELECT * FROM doodad_func_convert_fish_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new LootPackConvertFish
                        {
                            Id = reader.GetUInt32("id"),
                            ItemId = reader.GetUInt32("item_id"),
                            LootPackId = reader.GetUInt32("loot_pack_id"),
                            DoodadFuncConvertFishId = reader.GetUInt32("doodad_func_convert_fish_id")
                        };
                        List<LootPackConvertFish> lootPackConvertFish;
                        if (_lootPackConvertFish.TryGetValue(template.ItemId, out var value))
                            lootPackConvertFish = value;
                        else
                        {
                            lootPackConvertFish = [];
                            _lootPackConvertFish.Add(template.ItemId, lootPackConvertFish);
                        }

                        lootPackConvertFish.Add(template);
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

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM unit_modifiers WHERE owner_type='Item'";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
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

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM armor_grade_buffs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    var ArmorType = 1u;
                    var ItemGrade = 4u;
                    while (reader.Read())
                    {
                        var armorGradeBuff = new ArmorGradeBuff();
                        armorGradeBuff.Id = reader.GetByte("id");
                        //armorGradeBuff.ArmorType = (ArmorType)reader.GetUInt32("armor_type_id"); // there is no such field in the database for version 3.0.3.0
                        //armorGradeBuff.ItemGrade = (ItemGrade)reader.GetUInt32("item_grade_id"); // there is no such field in the database for version 3.0.3.0
                        armorGradeBuff.BuffId = reader.GetUInt32("buff_id");

                        _armorGradeBuffs.TryAdd((ArmorType)ArmorType, new Dictionary<ItemGrade, ArmorGradeBuff>());
                        _armorGradeBuffs[(ArmorType)ArmorType].TryAdd((ItemGrade)ItemGrade, armorGradeBuff);

                        // hardcoded fix
                        ItemGrade++;
                        if (ItemGrade != 12) { continue; }
                        ArmorType++;
                        ItemGrade = 4;
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM item_sets";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var entry = new ItemSet();
                        entry.Id = reader.GetUInt32("id");
                        entry.KindId = reader.GetUInt32("kind_id");
                        //entry.Name = reader.GetString("name"); // there is no such field in the database for version 3.0.3.0

                        if (!_itemSets.TryAdd(entry.Id, entry))
                            Logger.Warn($"Duplicate entry for item_sets {entry.Id}");
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM item_set_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var entry = new ItemSetItem()
                        {
                            Id = reader.GetUInt32("id"),
                            ItemSetId = reader.GetUInt32("item_set_id"),
                            ItemId = reader.GetUInt32("item_id"),
                            Count = reader.GetInt32("count"),
                        };

                        if (_itemSets.TryGetValue(entry.ItemSetId, out var itemSet))
                        {
                            itemSet.Items.TryAdd(entry.Id, entry);
                        }
                        else
                        {
                            Logger.Warn($"Missing item set entry for item_set_items {entry.Id}");
                        }

                    }
                }
            }

            // Read Item grade related info
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM item_slave_equipments";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var template = new ItemSlaveEquip();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.DoodadScale = reader.GetFloat("doodad_scale");
                        template.DoodadId = reader.GetUInt32("doodad_id");
                        template.RequireItemId = reader.GetUInt32("require_item_id");
                        template.SlaveEquipPackId = reader.GetUInt32("slave_equip_pack_id");
                        template.SlaveId = reader.GetUInt32("slave_id");
                        template.SlotPackId = reader.GetUInt32("slot_pack_id");
                        _ItemSlaveEquips.TryAdd(template.ItemId, template);
                    }
                }
            }

            // Search and Translation Help Items, as well as naming missing items names (has other templates, but not in items? Removed items maybe ?)
            var invalidItemCount = 0;
            foreach (var i in _templates)
            {
                if (i.Value.Name == null)
                {
                    invalidItemCount++;
                    i.Value.Name = "invalid_item_" + i.Value.Id;
                }
                i.Value.searchString = (i.Value.Name + " " + LocalizationManager.Instance.Get("items", "name", i.Value.Id)).ToLower();
            }

            Logger.Info($"Loaded {_templates.Count} item templates (with {invalidItemCount} unused) ...");
        }

        OnItemsLoaded?.Invoke(this, EventArgs.Empty);
        _loaded = true;
    }

    public Item GetItemByItemId(ulong itemId)
    {
        return _allItems.GetValueOrDefault(itemId);
    }

    public uint GetDoodadIdByItemId(uint itemId)
    {
        return _ItemSlaveEquips.TryGetValue(itemId, out var item) ? item.DoodadId : 0;
    }

    public uint GetSlaveIdByItemId(uint itemId)
    {
        return _ItemSlaveEquips.TryGetValue(itemId, out var item) ? item.SlaveId : 0;
    }

    public (int, int, int) Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        var deleteCount = 0;
        var updateCount = 0;
        var containerUpdateCount = 0;
        // Logger.Info("Saving items data ...");

        // Remove deleted items from DB
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;
            lock (_removedItems)
            {
                if (_removedItems.Count > 0)
                {
                    using (var deleteCommand = connection.CreateCommand())
                    {
                        var removedItemList = string.Join(",", _removedItems);
                        deleteCommand.CommandText = $"DELETE FROM items WHERE `id` IN({removedItemList})";
                        deleteCommand.Prepare();
                        deleteCount += deleteCommand.ExecuteNonQuery();
                    }

                    if (deleteCount != _removedItems.Count)
                        Logger.Error($"Some items could not be deleted, only {deleteCount}/{_removedItems.Count} items removed !");
                    _removedItems.Clear();
                }
            }
            // Update items
        }

        // Update Container Info
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            lock (_allPersistentContainers)
            {
                foreach (var (_, c) in _allPersistentContainers)
                {
                    if (c.ContainerType == SlotType.None)
                        continue; // Skip the BuyBack container

                    if (c.ContainerId <= 0)
                        continue;

                    if (c.IsDirty == false)
                        continue;

                    command.CommandText = "REPLACE INTO item_containers (" +
                                          "`container_id`,`container_type`,`slot_type`,`container_size`,`owner_id`,`mate_id`" +
                                          ") VALUES ( " +
                                          "@container_id, @container_type, @slot_type, @container_size, @owner_id, @mate_id" +
                                          ")";

                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@container_id", c.ContainerId);
                    command.Parameters.AddWithValue("@container_type", c.ContainerTypeName());
                    command.Parameters.AddWithValue("@slot_type", c.ContainerType.ToString());
                    command.Parameters.AddWithValue("@container_size", c.ContainerSize);
                    command.Parameters.AddWithValue("@owner_id", c.OwnerId);
                    command.Parameters.AddWithValue("@mate_id", c.MateId);
                    try
                    {
                        var res = command.ExecuteNonQuery();
                        containerUpdateCount += res;
                        if (res > 0)
                            c.IsDirty = false;
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e);
                    }
                }
            }
        }


        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.Transaction = transaction;

            lock (_allItems)
            {
                foreach (var (itemId, item) in _allItems)
                {
                    if (item == null)
                        continue;

                    if (item.SlotType == SlotType.None)
                    {
                        // Only give an error if it has no owner, otherwise it's likely a BuyBack item
                        if (item.OwnerId <= 0)
                            continue;

                        // Try to re-attain the slot type by getting the owning container's type
                        if (item._holdingContainer != null)
                        {
                            item.SlotType = GetContainerSlotTypeByContainerId(item._holdingContainer.ContainerId);
                        }

                        // If the slot type changed, give a warning, otherwise skip this save
                        if (item.SlotType != SlotType.None)
                        {
                            Logger.Warn($"Slot type for {item.Id} was None, changing to {item.SlotType}");
                        }
                        else
                        {
                            continue;
                        }
                    }
                    if (!Enum.IsDefined(typeof(SlotType), item.SlotType))
                    {
                        Logger.Warn($"Found SlotType.{item.SlotType} in itemslist, skipping ID:{itemId} - Template:{item.TemplateId}");
                        continue;
                    }

                    if (!item.IsDirty)
                        continue;

                    var details = new Commons.Network.PacketStream();
                    item.WriteDetails(details);

                    command.CommandText = "REPLACE INTO items (" +
                        "`id`,`type`,`template_id`,`container_id`,`slot_type`,`slot`,`count`,`details`,`lifespan_mins`,`made_unit_id`," +
                        "`unsecure_time`,`unpack_time`,`owner`,`created_at`,`grade`,`flags`,`ucc`," +
                        "`expire_time`,`expire_online_minutes`,`charge_time`,`charge_count`" +
                        ") VALUES ( " +
                        "@id, @type, @template_id, @container_id, @slot_type, @slot, @count, @details, @lifespan_mins, @made_unit_id, " +
                        "@unsecure_time,@unpack_time,@owner,@created_at,@grade,@flags,@ucc," +
                        "@expire_time,@expire_online_minutes,@charge_time,@charge_count" +
                        ")";

                    command.Parameters.AddWithValue("@id", item.Id);
                    command.Parameters.AddWithValue("@type", item.GetType().ToString());
                    command.Parameters.AddWithValue("@template_id", item.TemplateId);
                    command.Parameters.AddWithValue("@container_id", item._holdingContainer?.ContainerId ?? 0);
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
                    command.Parameters.AddWithValue("@ucc", item.UccId);
                    command.Parameters.AddWithValue("@expire_time", item.ExpirationTime);
                    command.Parameters.AddWithValue("@expire_online_minutes", item.ExpirationOnlineMinutesLeft);
                    command.Parameters.AddWithValue("@charge_time", item.ChargeStartTime);
                    command.Parameters.AddWithValue("@charge_count", item.ChargeCount);

                    try
                    {
                        if (command.ExecuteNonQuery() < 1)
                        {
                            Logger.Error($"Error updating items {item.Id} ({item.TemplateId}) !");
                        }
                        else
                        {
                            item.IsDirty = false;
                            updateCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        // Create a manual SQL string with the data provided
                        var sqlString = $"REPLACE INTO items (id, type, template_id, container_id, slot_type, slot, count, details, lifespan_mins, made_unit_id, unsecure_time, unpack_time, owner, created_at, grade, flags, ucc, expire_time, expire_online_minutes, charge_time, charge_count) VALUES ({item.Id}, {item.GetType()}, {item.TemplateId}, {item._holdingContainer?.ContainerId ?? 0}, {item.SlotType}, {item.Slot}, {item.Count}, {details.GetBytes()}, {item.LifespanMins}, {item.MadeUnitId}, {item.UnsecureTime}, {item.UnpackTime}, {item.CreateTime}, {item.OwnerId}, {item.Grade}, {(byte)item.ItemFlags}, {item.UccId}, {item.ExpirationTime}, {item.ExpirationOnlineMinutesLeft}, {item.ChargeStartTime}, {item.ChargeCount})";

                        Logger.Error($"Error: {ex.Message}\nSQL Query: {sqlString}\n");
                    }
                    command.Parameters.Clear();
                }
            }
        }

        return (updateCount, deleteCount, containerUpdateCount);
    }

    private SlotType GetContainerSlotTypeByContainerId(ulong dbId)
    {
        _allPersistentContainers.TryGetValue(dbId, out var container);

        if (container != null)
        {
            return container.ContainerType;
        }

        return SlotType.None;
    }

    /// <summary>
    /// Grabs an existing container owned by a player
    /// </summary>
    /// <param name="characterId">Player Id</param>
    /// <param name="slotType">Container Type to get</param>
    /// <param name="parentUnit">If set, overrides the Parent Unit</param>
    /// <param name="mateId">Mate Id if a new container needs to be created</param>
    /// <returns></returns>
    public ItemContainer GetItemContainerForCharacter(uint characterId, SlotType slotType, Unit parentUnit, uint mateId)
    {
        foreach (var c in _allPersistentContainers.Values)
        {
            if (c.OwnerId == characterId && c.ContainerType == slotType && c.MateId == mateId)
            {
                if (parentUnit != null)
                    c.ParentUnit = parentUnit;
                return c;
            }
        }

        var newContainerType = slotType switch
        {
            SlotType.Equipment => "EquipmentContainer",
            SlotType.EquipmentMate => "MateEquipmentContainer",
            SlotType.EquipmentSlave => "SlaveEquipmentContainer",
            _ => "ItemContainer"
        };

        var newContainer = ItemContainer.CreateByTypeName(newContainerType, characterId, slotType, slotType != SlotType.None, parentUnit);

        if (slotType != SlotType.None)
            _allPersistentContainers.Add(newContainer.ContainerId, newContainer);

        if (mateId > 0)
            newContainer.MateId = mateId;

        return newContainer;
    }

    public bool CheckItemContainerForCharacter(uint characterId, SlotType slotType, uint mateId = 0)
    {
        return _allPersistentContainers.Any(c => c.Value.OwnerId == characterId && c.Value.ContainerType == slotType && c.Value.MateId == mateId);
    }

    public CofferContainer NewCofferContainer(uint characterId)
    {
        var coffer = new CofferContainer(characterId, true);
        _allPersistentContainers.Add(coffer.ContainerId, coffer);
        return coffer;
    }

    public ItemContainer GetItemContainerByDbId(ulong dbId)
    {
        return _allPersistentContainers.GetValueOrDefault(dbId);
    }

    /// <summary>
    /// Deletes a ItemContainer from DB if it's empty
    /// </summary>
    /// <param name="container"></param>
    /// <returns></returns>
    public bool DeleteItemContainer(ItemContainer container)
    {
        if (container == null)
            return true;

        if (container.Items.Count > 0)
            return false;

        var idToRemove = (uint)container.ContainerId;
        container.ContainerId = 0;

        bool res;
        lock (_allPersistentContainers)
        {
            res = _allPersistentContainers.Remove(idToRemove);
            ContainerIdManager.Instance.ReleaseId(idToRemove);
        }

        // Remove deleted container from DB
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.Connection = connection;
        using var deleteCommand = connection.CreateCommand();
        deleteCommand.CommandText = "DELETE FROM item_containers WHERE `container_id` = @id";
        deleteCommand.Parameters.Clear();
        deleteCommand.Parameters.AddWithValue("@id", idToRemove);
        deleteCommand.Prepare();
        if (deleteCommand.ExecuteNonQuery() <= 0)
            Logger.Error($"Failed to delete ItemContainer from DB container_id: {idToRemove}");

        return res;
    }

    public void LoadUserItems()
    {
        if (_loadedUserItems)
            return;

        Logger.Info("Loading user items ...");
        _allItems = new Dictionary<ulong, Item>();
        _allPersistentContainers = new Dictionary<ulong, ItemContainer>();

        // No lock needed here since this is the first and only time it gets assigned a new list
        // ReSharper disable once InconsistentlySynchronizedField
        _removedItems = new List<ulong>();

        using (var connection = MySQL.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_containers ;";

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var containerId = reader.GetUInt32("container_id");
                    var containerType = reader.GetString("container_type");
                    var slotType = (SlotType)Enum.Parse(typeof(SlotType), reader.GetString("slot_type"), true);
                    var containerSize = reader.GetInt32("container_size");
                    var containerOwnerId = reader.GetUInt32("owner_id");
                    var containerMateId = reader.GetUInt32("mate_id");
                    var container = ItemContainer.CreateByTypeName(containerType, containerOwnerId, slotType, false, null);
                    container.ContainerId = containerId;
                    container.ContainerSize = containerSize;
                    container.MateId = containerMateId;

                    _allPersistentContainers.Add(container.ContainerId, container);
                    container.IsDirty = false;
                }
            }

            command.CommandText = "SELECT * FROM items ;";

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var itemType = reader.GetString("type");
                    var itemId = reader.GetUInt64("id");
                    var itemTemplateId = reader.GetUInt32("template_id");
                    Type nClass = null;
                    try
                    {
                        nClass = Type.GetType(itemType);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex);
                    }

                    if (nClass == null)
                    {
                        Logger.Warn($"Item type {itemType} not found for id {itemId}!");
                        var itemTemplate = GetTemplate(itemTemplateId);
                        if (itemTemplate == null)
                        {
                            Logger.Error($"Unable to restore template {itemTemplateId} for item {itemId}, item will not be loaded!");
                            continue;
                        }
                        Logger.Info($"Item {itemId} defined as {itemType} in the database is being restored using template {itemTemplate.Id} with class {itemTemplate.ClassType}");
                        nClass = itemTemplate.ClassType;
                    }

                    Item item;
                    try
                    {
                        item = (Item)Activator.CreateInstance(nClass);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex);
                        Logger.Error(ex.InnerException);
                        item = new Item();
                    }

                    if (item == null)
                    {
                        Logger.Error($"Failed to create item: itemId {itemId}, itemTemplateId {itemTemplateId}, nClass {nameof(nClass)}");
                        continue;
                    }

                    item.Id = itemId;
                    item.OwnerId = reader.GetUInt64("owner");
                    item.TemplateId = itemTemplateId;
                    item.Template = GetTemplate(item.TemplateId);
                    var containerId = reader.GetUInt64("container_id");
                    var slotTypeString = reader.GetString("slot_type");
                    if (Enum.IsDefined(typeof(SlotType), slotTypeString))
                        item.SlotType = (SlotType)Enum.Parse(typeof(SlotType), slotTypeString, true);
                    else
                        item.SlotType = SlotType.System;
                    var thisItemSlot = reader.GetInt32("slot");
                    item.Slot = thisItemSlot;
                    item.Count = reader.GetInt32("count");
                    item.LifespanMins = reader.GetInt32("lifespan_mins");
                    item.MadeUnitId = reader.GetUInt32("made_unit_id");
                    item.UnsecureTime = reader.GetDateTime("unsecure_time");
                    item.UnpackTime = reader.GetDateTime("unpack_time");
                    item.CreateTime = reader.GetDateTime("created_at");
                    item.ItemFlags = (ItemFlag)reader.GetByte("flags");
                    item.UccId = reader.GetUInt32("ucc"); // Make sure this UCC is set BEFORE reading details as UccItem needs to be able to override it
                    var details = (Commons.Network.PacketStream)(byte[])reader.GetValue("details");
                    item.ReadDetails(details);

                    // Overwrite Fixed-grade items, just to make sure. Retail does not do this, but it just feels better if we do
                    if (item.Template.FixedGrade >= 0)
                        item.Grade = (byte)item.Template.FixedGrade;
                    else if (item.Template.Gradable)
                        item.Grade = reader.GetByte("grade"); // Load from our DB if the item is gradable

                    item.ExpirationTime = reader.IsDBNull("expire_time") ? DateTime.MinValue : reader.GetDateTime("expire_time");
                    item.ExpirationOnlineMinutesLeft = reader.GetDouble("expire_online_minutes");
                    item.ChargeStartTime = reader.IsDBNull("charge_time") ? DateTime.MinValue : reader.GetDateTime("charge_time");
                    item.ChargeCount = reader.GetInt16("charge_count");

                    // Add it to the global pool
                    if (!_allItems.TryAdd(item.Id, item))
                    {
                        ReleaseId(item.Id);
                        Logger.Error($"Failed to load item with ID {item.Id}, possible duplicate entries!");
                    }

                    if ((containerId > 0) && _allPersistentContainers.TryGetValue(containerId, out var container))
                    {
                        // Move item to its container (if defined)
                        if (container.AddOrMoveExistingItem(ItemTaskType.Invalid, item, item.Slot))
                            item.IsDirty = false;
                        else
                            Logger.Fatal($"Failed to add item {item} to existing container {container.ContainerId} !");
                    }
                    else
                    {
                        Logger.Trace($"Can't find a container for Item {item.Id} ({item.Template.Name}), ContainerId: {containerId}");
                        // This Item does not have a valid container it can fit in

                        if (item.OwnerId > 0)
                        {
                            // Item does have an owner, let's try to create a valid container for it
                            var cContainer = GetItemContainerForCharacter((uint)item.OwnerId, item.SlotType, null, 0);
                            if (cContainer.AddOrMoveExistingItem(ItemTaskType.Invalid, item, item.Slot))
                            {
                                item.Slot = thisItemSlot;
                                item.IsDirty = true;
                            }
                            else
                            {
                                Logger.Fatal($"Failed to add owned item ({item.Id}){item} to new container (Id:{cContainer.ContainerId}) !");
                                item.Slot = thisItemSlot;
                                item.IsDirty = false;
                            }
                        }
                        else
                        {
                            Logger.Warn($"Could not find a new container for Orphaned item {item.Id} ({item.TemplateId}, ContainerId: {containerId}");
                            item.Slot = thisItemSlot; // Override the slot number again in case things didn't go as planned
                            item.IsDirty = false;
                        }
                    }
                }
            }
        }

        Logger.Info("Starting Timed Items Task ...");
        var itemTimerTask = new ItemTimerTask();
        TaskManager.Instance.Schedule(itemTimerTask, null, TimeSpan.FromSeconds(1));

        _loadedUserItems = true;
    }

    /// <summary>
    /// Gets a new itemID for use on new items, will also remove it from the deleted itemIDs list. Use this instead of directly calling ItemIdManager.Instance.GetNextId();
    /// </summary>
    /// <returns>A new itemID</returns>
    private ulong GetNewId()
    {
        var itemId = ItemIdManager.Instance.GetNextId();
        if (_removedItems != null)
        {
            lock (_removedItems)
            {
                if (itemId != 0 && _removedItems.Contains(itemId))
                    _removedItems.Remove(itemId);
            }
        }

        return itemId;
    }

    /// <summary>
    /// Releases a itemId for re-use, will also add it to the removed items list, use instead of ItemIdManager.Instance.ReleaseId();
    /// </summary>
    /// <param name="itemId">itemId of the item to be freed up</param>
    public void ReleaseId(ulong itemId)
    {
        if (_removedItems != null)
        {
            lock (_removedItems)
            {
                if (itemId != 0 && !_removedItems.Contains(itemId))
                    _removedItems.Add(itemId);
            }
        }

        if (_allItems != null)
        {
            lock (_allItems)
            {
                if (_allItems.ContainsKey(itemId))
                    _allItems.Remove(itemId);
            }
        }

        // This should be the only place where ItemId ReleaseId should be called directly
        ItemIdManager.Instance.ReleaseId((uint)itemId);
    }

    [Obsolete("You can now use directly linked item containers, and no longer need to load them into the character object")]
    public List<Item> LoadPlayerInventory(ICharacter character)
    {
        var res = (from i in _allItems where i.Value.OwnerId == character.Id select i.Value).ToList();
        return res;
    }

    private void OnSkillsLoaded(object sender, EventArgs e)
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
        return template is BackpackTemplate && (!template.BindType.HasFlag(ItemBindType.BindOnEquip));
    }

    private static int UpdateItemContainerTimers(TimeSpan delta, ItemContainer itemContainer, Character character)
    {
        var res = 0;
        if (itemContainer == null)
        {
            Logger.Error("Invalid itemContainer when processing item timers");
            return res;
        }

        var isEquipmentContainer = (itemContainer is EquipmentContainer);

        for (var i = itemContainer.Items.Count - 1; i >= 0; i--)
        {
            var item = itemContainer.Items[i];
            var doExpire = false;

            // Check if buffs need to expire
            if (isEquipmentContainer && (item is EquipItem { Template: EquipItemTemplate { RechargeBuffId: > 0 } equipItemTemplate } equipItem))
            {
                var expireBuff = false;

                // Expire Time
                var expireCheckTime = (equipItemTemplate.BindType == ItemBindType.BindOnUnpack)
                    ? equipItem.UnpackTime
                    : equipItem.ChargeStartTime;
                expireCheckTime = expireCheckTime.AddMinutes(equipItemTemplate.ChargeLifetime);

                // Do we need to check if charges expired ?
                var checkCharges = (equipItemTemplate.ChargeCount > 0);
                if ((equipItemTemplate.BindType == ItemBindType.BindOnUnpack) && (equipItem.HasFlag(ItemFlag.Unpacked) == false))
                    checkCharges = false;

                // Timed Charged items
                if ((equipItemTemplate.ChargeLifetime > 0) && (expireCheckTime <= DateTime.UtcNow))
                    expireBuff = true;

                // Count Charged Items
                if (checkCharges && (equipItemTemplate.ChargeCount > 0) && (equipItem.ChargeCount <= 0))
                    expireBuff = true;

                // Apply the "expire" buff if needed
                if (expireBuff && (character != null) &&
                    character.Buffs.CheckBuff(equipItemTemplate.RechargeBuffId))
                    character.Buffs.RemoveBuff(equipItemTemplate.RechargeBuffId);
            }

            // Check if item itself needs to be expired
            if ((item.ExpirationTime > DateTime.MinValue) && (item.ExpirationTime <= DateTime.UtcNow))
                doExpire = true; // Item expired by predefined end time
            else if (item.ExpirationOnlineMinutesLeft > 0.0)
            {
                item.ExpirationOnlineMinutesLeft -= delta.TotalMinutes; // reduce lifespan of this item
                if (item.ExpirationOnlineMinutesLeft <= 0.0)
                    doExpire = true; // online timed lifespan is done
            }

            if (doExpire)
            {
                res++;
                var sync = ExpireItemPacket(item);
                if (sync != null)
                    character?.SendPacket(sync);
                itemContainer.RemoveItem(ItemTaskType.LifespanExpiration, item, true);
            }
        }

        return res;
    }

    public void UpdateItemTimers()
    {
        TimeSpan delta;
        lock (ItemTimerLock)
        {
            var now = DateTime.UtcNow;
            delta = now - LastTimerCheck;
            LastTimerCheck = now;
        }

        // Logger.Trace($"UpdateItemTimers - Tick, Delta: {delta.TotalMilliseconds}ms");

        // Timers are actually only checked when it's owner is actually online, so we loop the online characters for this.
        // You can clearly see this on retail after event items expired when you were offline, they will expire immediately
        // even before you get the welcome message when logging in. (you can see it in the logs)
        // It only does this for items in your inventory, equipment and warehouse,
        // it is for example possible to have one in your mailbox, and it will immediately expire when you take it out.
        var onlinePlayers = WorldManager.Instance.GetAllCharacters();
        var res = 0;
        foreach (var character in onlinePlayers)
        {
            res += UpdateItemContainerTimers(delta, character?.Inventory?.Equipment, character);
            res += UpdateItemContainerTimers(delta, character?.Inventory?.Bag, character);
            res += UpdateItemContainerTimers(delta, character?.Inventory?.Warehouse, character);
        }

        if (res > 0)
            Logger.Warn($"{res} item(s) expired and have been removed.");
    }

    public static GamePacket SetItemExpirationTime(Item item, DateTime newTime)
    {
        if (item.ExpirationTime != newTime)
        {
            item.ExpirationTime = newTime;
            Logger.Warn($"Set ExpirationTime for item {item.Id}, {item.Template.Name} set to {newTime}");
            return new SCSyncItemLifespanPacket(newTime > item.CreateTime, item.Id, item.TemplateId, newTime);
        }

        return null;
    }

    public static GamePacket SetItemOnlineExpirationTime(Item item, double newMinutes)
    {
        // if (item.ExpirationOnlineMinutesLeft != newMinutes)
        if (Math.Abs(item.ExpirationOnlineMinutesLeft - newMinutes) > double.Epsilon)
        {
            var newTime = DateTime.UtcNow.AddMinutes(newMinutes);
            item.ExpirationOnlineMinutesLeft = newMinutes;
            Logger.Warn($"Set ExpirationOnlineMinutesLeft for item {item.Id}, {item.Template.Name} set to {newTime}");
            return new SCSyncItemLifespanPacket(newMinutes >= 0.0, item.Id, item.TemplateId, newTime);
        }

        return null;
    }

    public static GamePacket ExpireItemPacket(Item item)
    {
        item.ExpirationTime = DateTime.MinValue;
        item.ExpirationOnlineMinutesLeft = 0.0;
        return new SCSyncItemLifespanPacket(false, item.Id, item.TemplateId, DateTime.MinValue);
    }

    public bool UnwrapItem(Character character, SlotType slotType, byte slot, ulong itemId)
    {
        var item = GetItemByItemId(itemId);
        if (item == null)
            return false;
        if ((item.SlotType != slotType) || (item.Slot != slot))
        {
            Logger.Warn($"UnwrapItem: Requested item position does not match up for {itemId} of user {character.Name}");
            return false;
        }
        item.UnpackTime = DateTime.UtcNow;//.AddDays(-30).AddSeconds(15);
        item.SetFlag(ItemFlag.Unpacked);
        if (item.Template.BindType == ItemBindType.BindOnUnpack)
            item.SetFlag(ItemFlag.SoulBound);
        var updateItemTask = new ItemUpdateSecurity(item, (byte)item.ItemFlags, item.HasFlag(ItemFlag.Secure), item.HasFlag(ItemFlag.Secure), item.ItemFlags.HasFlag(ItemFlag.Unpacked));
        character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.ItemTaskThistimeUnpack, updateItemTask, new List<ulong>()));
        if ((item.Template is EquipItemTemplate { ChargeLifetime: > 0 }))
            character.SendPacket(new SCSyncItemLifespanPacket(true, item.Id, item.TemplateId, item.UnpackTime));
        return true;
    }

    public ItemSet GetItemSet(uint itemSetId)
    {
        return _itemSets.GetValueOrDefault(itemSetId);
    }
}
