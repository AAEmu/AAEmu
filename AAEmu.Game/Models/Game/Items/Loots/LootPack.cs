using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Items.Loots;

public class LootPack
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public uint Id { get; init; }
    public uint GroupCount { get; set; }
    public List<Loot> Loots { get; init; }
    public Dictionary<uint, LootGroups> Groups { get; init; }
    public Dictionary<uint, LootActabilityGroups> ActabilityGroups { get; init; }
    public Dictionary<uint, List<Loot>> LootsByGroupNo { get; init; }

    // unused private List<(uint itemId, int count, byte grade)> _generatedPack;

    /// <summary>
    /// Generates the contents of a LootPack, in the form of a list of tuples. This list is stored internally
    /// </summary>
    /// <param name="player">Player whose loot multipliers need to be used</param>
    /// <param name="actabilityType">Actability that triggered the Loot generation</param>
    /// <returns></returns>
    public List<(uint itemId, int count, byte grade, uint originalGroup)> GeneratePack(Character player, ActabilityType actabilityType)
    {
        var lootDropRate = (100f + player.DropRateMul) / 100f;
        var lootGoldRate = (100f + player.LootGoldMul) / 100f;
        return GeneratePackNew(lootDropRate, lootGoldRate, player, actabilityType, false);
    }

    /// <summary>
    /// Generates the contents of a LootPack, in the form of a list of tuples. This list is stored internally
    /// </summary>
    /// <param name="lootDropRate">1.0f = 100%</param>
    /// <param name="lootGoldRate">1.0f = 100% applies to coins item only</param>
    /// <param name="player">The player the loot is generated for, currently only used to handle exclusions</param>
    /// <returns></returns>
    private List<(uint itemId, int count, byte grade)> GeneratePack(float lootDropRate, float lootGoldRate, ICharacter player)
    {
        // Use 8000022 as an example

        var items = new List<(uint itemId, int count, byte grade)>();

        // Logger.Info($"Rolling loot pack {Id} containing max group Id: {GroupCount}");

        // For every group
        for (uint gIdx = 0; gIdx <= GroupCount; gIdx++)
        {
            var hasLootGroup = false;
            var lootGradeDistributionId = 0u;
            var alwaysDropGroup = gIdx == 0;

            if (!LootsByGroupNo.ContainsKey(gIdx))
                continue;

            // Logger.Debug($"Rolling loot with pack {Id}, Group {gIdx}/{GroupCount}, checking Groups conditions");

            // If that group has a LootGroup, roll the dice
            if (Groups.TryGetValue(gIdx, out var lootGroup))
            {
                hasLootGroup = true;
                lootGradeDistributionId = lootGroup.ItemGradeDistributionId;
                var dice = (long)Rand.Next(0, 10000000);

                // Use generic loot multiplier for the groups ?
                dice = (long)Math.Floor(dice / (lootDropRate * AppConfiguration.Instance.World.LootRate));

                // Logger.Debug($"Rolling loot with pack {Id}, GroupNo {gIdx} rolled {dice}/{lootGroup.DropRate}");

                if ((lootGroup.DropRate > 0) && (dice > lootGroup.DropRate))
                    continue;
            }

            // Logger.Debug($"Rolling loot with pack {Id}, Group {gIdx}/{GroupCount}, checking ActAbilityGroups conditions");

            // If that group has a LootActGroup, roll the dice
            if (ActabilityGroups.TryGetValue(gIdx, out var actabilityGroup))
            {
                var dice = (long)Rand.Next(0, 10000);

                // Use generic loot multiplier for the ActGroups ?
                dice = (long)Math.Floor(dice / (lootDropRate * AppConfiguration.Instance.World.LootRate));

                // Logger.Debug($"Rolling loot with pack {Id}, ActAbilityGroupNo {gIdx} rolled {dice}/{actabilityGroup.MinDice}~{actabilityGroup.MaxDice}");

                // TODO: Use MinDice for something as well?
                if (dice > actabilityGroup.MaxDice)
                    continue;
            }

            var loots = LootsByGroupNo[gIdx];
            if (loots == null || loots.Count == 0)
                continue;

            var uniqueItemDrop = loots[0].DropRate == 1;
            var itemRoll = Rand.Next(0, 10000000);

            // Apply multiplier for loot drop rate
            itemRoll = (int)Math.Round(itemRoll / lootDropRate);

            var itemStackingRoll = 0u;

            List<Loot> selected = [];

            if ((alwaysDropGroup == false) && (uniqueItemDrop || hasLootGroup || (GroupCount <= 1)))
            {
                selected.Add(loots.RandomElementByWeight(l => l.DropRate));
            }
            else
            {
                selected.AddRange(loots.Where(loot => loot.AlwaysDrop || loot.DropRate == 10000000 || alwaysDropGroup).ToList());

                foreach (var loot in loots.Where(loot => !(loot.AlwaysDrop || loot.DropRate == 10000000 || alwaysDropGroup)))
                {
                    if (alwaysDropGroup)
                    {
                        selected.Add(loot);
                        continue;
                    }
                    if (loot.DropRate + itemStackingRoll < itemRoll)
                    {
                        itemStackingRoll += loot.DropRate;
                        continue;
                    }

                    // itemStackingRoll += loot.DropRate;

                    selected.Add(loot);
                    break;
                }
            }

            foreach (var selectedPack in selected)
            {
                // If it's a quest item, check if target player has the quest active, else skip it.
                if (player != null)
                {
                    var itemTemplate = ItemManager.Instance.GetTemplate(selectedPack.ItemId);
                    if (itemTemplate?.LootQuestId > 0 && !player.Quests.HasQuest(itemTemplate.LootQuestId))
                        continue;
                }

                var lootCount = Rand.Next(selectedPack.MinAmount, selectedPack.MaxAmount + 1);

                var grade = selectedPack.GradeId;
                if (lootGradeDistributionId > 0)
                    grade = GetGradeFromDistribution(lootGradeDistributionId);

                // Multiply gold as needed
                if (selectedPack.ItemId == Item.Coins)
                    lootCount = (int)Math.Round(lootCount * (lootGoldRate * AppConfiguration.Instance.World.GoldLootMultiplier));

                items.Add((selectedPack.ItemId, lootCount, grade));
            }
        }

        // unused _generatedPack = items;
        return items;
    }

    /// <summary>
    /// Generates the contents of a LootPack, in the form of a list of tuples. This list is stored internally
    /// New experimental version
    /// </summary>
    /// <param name="lootDropRate">1.0f = 100%</param>
    /// <param name="lootGoldRate">1.0f = 100% applies to coins item only</param>
    /// <param name="player">The player the loot is generated for, currently only used to handle exclusions</param>
    /// <param name="actabilityType">AbilityType used to initiate the loot generation (used to calculate bonus)</param>
    /// <param name="doNotPreFilter"></param>
    /// <returns></returns>
    public List<(uint itemId, int count, byte grade, uint lootGroupOrigin)> GeneratePackNew(float lootDropRate, float lootGoldRate, Character player, ActabilityType actabilityType, bool doNotPreFilter)
    {
        var items = new List<(uint itemId, int count, byte grade, uint lootGroupOrigin)>();

        foreach (var (groupNo, groupLootList) in LootsByGroupNo)
        {
            var group = Groups.Values.FirstOrDefault(g => g.GroupNo == groupNo);
            // If group is defined, use it's DropRate for calculations 
            var groupRate = group is { DropRate: > 0 } ? group.DropRate / 100_000f : 1f;

            var selectedItemsByGroup = new Dictionary<uint, List<Loot>>();

            foreach (var loot in groupLootList)
            {
                // Check if this loot uses ActAbilityGroup dice
                var actGroup = ActabilityGroups.Values.FirstOrDefault(g => g.GroupId == loot.Group);
                if (actGroup != null)
                {
                    var actDice = (long)Rand.Next(0, 10_000);
                    // Use generic loot multiplier for the ActGroups ?
                    actDice = (long)Math.Floor(actDice / (lootDropRate * AppConfiguration.Instance.World.LootRate));

                    var actLevelMultiplier = 1f;
                    if ((player != null) && (player.Actability.Actabilities.TryGetValue((byte)actabilityType, out var actAbility)))
                    {
                        actLevelMultiplier *= actAbility.GetLootMultiplier();
                    }

                    // TODO: Use MinDice for something as well?
                    // TODO: Make ActAbility skill level of the player matter
                    if (actDice * actLevelMultiplier > actGroup.MaxDice)
                    {
                        continue;
                    }
                }

                // Check for Quest loot drops
                var itemTemplate = ItemManager.Instance.GetTemplate(loot.ItemId);
                if (itemTemplate?.LootQuestId > 0)
                {
                    if (!player.Quests.HasQuest(itemTemplate.LootQuestId))
                        continue;
                }

                // Group 0 items will always need to be included
                if (loot.Group <= 0 || loot.AlwaysDrop || loot.DropRate == 10000000)
                {
                    ref var lootList = ref CollectionsMarshal.GetValueRefOrAddDefault(selectedItemsByGroup, 0u, out var exists); // replaced loot.Group==0 with 0 to make it easier to see.
                    if (!exists)
                        lootList = [];
                    lootList.Add(loot);
                    continue;
                }

                var itemRate = loot.DropRate > 1 ? loot.DropRate / 10_000_000f : 1f;
                var requiresDice = (long)Math.Floor(10_000_000f * groupRate * itemRate * lootDropRate);
                var dice = (long)Rand.Next(0, 10000000);
                if (dice < requiresDice)
                {
                    if (!selectedItemsByGroup.ContainsKey(loot.Group))
                        selectedItemsByGroup.Add(loot.Group, []);
                    selectedItemsByGroup[loot.Group].Add(loot);
                }
            }

            // No matches found
            if (selectedItemsByGroup.Count <= 0)
                continue;

            foreach (var (groupId, loots) in selectedItemsByGroup)
            {
                if (loots.Count <= 0)
                    continue;

                // Always include all selected items if it's group 0
                if (groupId < 1)
                {
                    foreach (var loot in loots)
                    {
                        // Roll amount
                        var countToAddNow = Random.Shared.Next(loot.MinAmount, loot.MaxAmount + 1);
                        // Check for gold multiplier
                        if (loot.ItemId == Item.Coins)
                            countToAddNow = (int)Math.Round(countToAddNow * lootGoldRate);
                        var generatedGrade = loot.GradeId;
                        if (group?.ItemGradeDistributionId > 0)
                        {
                            generatedGrade = GetGradeFromDistribution(group.ItemGradeDistributionId);
                        }
                        items.Add((loot.ItemId, countToAddNow, generatedGrade, loot.Group));
                    }
                    continue;
                }

                if (doNotPreFilter == false)
                {
                    // If it's from a group higher than 1, pick one at random
                    var rngItem = Random.Shared.Next(loots.Count);
                    // Roll amount
                    var countToAdd = Random.Shared.Next(loots[rngItem].MinAmount, loots[rngItem].MaxAmount + 1);
                    // Check for gold multiplier
                    if (loots[rngItem].ItemId == Item.Coins)
                        countToAdd = (int)Math.Round(countToAdd * lootGoldRate);
                    var generatedGrade = loots[rngItem].GradeId;
                    if (group?.ItemGradeDistributionId > 0)
                    {
                        generatedGrade = GetGradeFromDistribution(group.ItemGradeDistributionId);
                    }

                    items.Add((loots[rngItem].ItemId, countToAdd, generatedGrade, loots[rngItem].Group));
                }
                else
                {
                    // If not pre-filtering, then add all items of within the group instead of a random one
                    // The caller of this function will need to manually pick a result they want to use
                    foreach (var loot in loots)
                    {
                        // Roll amount
                        var countToAddNow = Random.Shared.Next(loot.MinAmount, loot.MaxAmount + 1);
                        // Check for gold multiplier
                        if (loot.ItemId == Item.Coins)
                            countToAddNow = (int)Math.Round(countToAddNow * lootGoldRate);
                        var generatedGrade = loot.GradeId;
                        if (group?.ItemGradeDistributionId > 0)
                        {
                            generatedGrade = GetGradeFromDistribution(group.ItemGradeDistributionId);
                        }
                        items.Add((loot.ItemId, countToAddNow, generatedGrade, loot.Group));
                    }
                }
            }
        }

        return items;
    }

    /// <summary>
    /// Helper function to help find the owning player of a killing unit, either the player itself or the owners of the unit
    /// </summary>
    /// <param name="killer">Unit doing the killing blow</param>
    /// <returns></returns>
#pragma warning disable CA1859
    private ICharacter GetPlayerUsingKiller(IBaseUnit killer)
#pragma warning restore CA1859
    {
        if (killer is Character character)
            return character;

        if (killer is Units.Mate { OwnerObjId: > 0 } mate)
        {
            var mateOwner = WorldManager.Instance.GetBaseUnit(mate.OwnerObjId);
            if (mateOwner is Character mateOwnerCharacter)
                return mateOwnerCharacter;
        }
        else
        if (killer is Slave { OwnerType: BaseUnitType.Character } slave)
        {
            var slaveOwner = WorldManager.Instance.GetBaseUnit(slave.OwnerObjId);
            if (slaveOwner is Character slaveOwnerCharacter)
                return slaveOwnerCharacter;
        }
        else
        if (killer is Doodad { OwnerType: DoodadOwnerType.Character } doodad)
        {
            var doodadOwner = WorldManager.Instance.GetBaseUnit(doodad.OwnerObjId);
            if (doodadOwner is Character slaveOwnerCharacter)
                return slaveOwnerCharacter;
        }

        return null;
    }

    /// <summary>
    /// Gives a lootpack to the specified player. It is possible to pass in a pre-generated list if we wanted to do some extra checks on our player's inventory.
    /// </summary>
    /// <param name="character"></param>
    /// <param name="actabilityType"></param>
    /// <param name="taskType"></param>
    /// <param name="generatedList"></param>
    public bool GiveLootPack(Character character, ActabilityType actabilityType, ItemTaskType taskType, List<(uint itemId, int count, byte grade, uint originalGroup)> generatedList = null)
    {
        // If it is not generated yet, generate loot pack info now
        generatedList ??= GeneratePack(character, actabilityType);

        var canAdd = true;
        // First check for room
        foreach (var (itemTemplateId, count, _, _) in generatedList)
        {
            if (itemTemplateId == Item.Coins)
                continue;
            var freeSpace = character.Inventory.Bag.SpaceLeftForItem(itemTemplateId);
            if (freeSpace < count)
            {
                canAdd = false;
                break;
            }

        }

        // Not enough room to give the items, give none
        if (!canAdd)
            return false;
        var coinCount = 0;
        // Distribute the items (and coins)
        foreach (var (itemTemplateId, count, grade, _) in generatedList)
        {
            if (itemTemplateId == Item.Coins)
            {
                coinCount += count;
                //Coins can drop from multiple groups on the same item, collating.
                continue;
            }

            // Get actual grade
            var itemTemplate = ItemManager.Instance.GetTemplate(itemTemplateId);
            var gradeToAdd = itemTemplate.FixedGrade > 0 ? itemTemplate.FixedGrade : grade > 1 ? grade : -1;

            if (!character.Inventory.TryAddNewItem(taskType, itemTemplateId, count, gradeToAdd))
            {
                Logger.Error($"Unable to give loot to {character.Name} - ItemId: {itemTemplate} x {count} at grade {gradeToAdd} (loot grade {grade})");
                return false;
            }
        }

        if (coinCount > 0)
        {
            //We have coins to give out.
            // Logger.Debug("{Category} - {Character} got {Amount} from lootpack {Lootpack}");
            character.AddMoney(SlotType.Inventory, coinCount, taskType);
        }

        return true;
    }

    private static byte GetGradeFromDistribution(uint id)
    {
        byte gradeId = 0;
        var distributions = ItemManager.Instance.GetGradeDistributions((byte)id);

        var array = new[]
        {
            distributions.Weight0, distributions.Weight1, distributions.Weight2, distributions.Weight3,
            distributions.Weight4, distributions.Weight5, distributions.Weight6, distributions.Weight7,
            distributions.Weight8, distributions.Weight9, distributions.Weight10, distributions.Weight11
        };

        var old = 0;
        var gradeDrop = Rand.Next(0, 100);
        for (byte i = 0; i <= 11; i++)
        {
            if (gradeDrop <= array[i] + old)
            {
                gradeId = i;
                i = 11;
            }
            else
            {
                old += array[i];
            }
        }

        return gradeId;
    }
}
