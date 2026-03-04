using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

using Moq;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ItemManagerTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_DoesNotCallDependencies()
    {
        // Arrange
        var mockSkill = new Mock<ISkillManager>();
        var mockItemId = new Mock<IItemIdManager>();
        var mockContainerId = new Mock<IContainerIdManager>();
        var mockLocale = new Mock<ILocalizationManager>();
        var mockTask = new Mock<ITaskManager>();
        var mockWorld = new Mock<IWorldManager>();

        // Act
        var manager = new ItemManager(
            mockSkill.Object,
            mockItemId.Object,
            mockContainerId.Object,
            mockLocale.Object,
            mockTask.Object,
            mockWorld.Object);

        // Assert
        Assert.NotNull(manager);
        mockSkill.VerifyNoOtherCalls();
        mockItemId.VerifyNoOtherCalls();
        mockContainerId.VerifyNoOtherCalls();
        mockLocale.VerifyNoOtherCalls();
        mockTask.VerifyNoOtherCalls();
        mockWorld.VerifyNoOtherCalls();
    }

    #endregion

    #region GetTemplate Tests

    [Fact]
    public void GetTemplate_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var manager = CreateItemManager();
        var template = new ItemTemplate
        {
            Id = 100,
            Name = "Test Item",
            Level = 10,
            Price = 50
        };
        var templates = new Dictionary<uint, ItemTemplate> { { 100, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(100);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100u, result.Id);
        Assert.Equal("Test Item", result.Name);
        Assert.Equal(10, result.Level);
        Assert.Equal(50, result.Price);
    }

    [Fact]
    public void GetTemplate_TemplateDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var templates = new Dictionary<uint, ItemTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetTemplate_ZeroId_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var templates = new Dictionary<uint, ItemTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(0);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetTemplate_MultipleTemplates_ReturnsCorrectTemplate()
    {
        // Arrange
        var manager = CreateItemManager();
        var template1 = new ItemTemplate { Id = 1, Name = "Item 1" };
        var template2 = new ItemTemplate { Id = 2, Name = "Item 2" };
        var template3 = new ItemTemplate { Id = 3, Name = "Item 3" };

        var templates = new Dictionary<uint, ItemTemplate>
        {
            { 1, template1 },
            { 2, template2 },
            { 3, template3 }
        };
        SetPrivateField(manager, "_templates", templates);

        // Act & Assert
        Assert.Equal(template1, manager.GetTemplate(1));
        Assert.Equal(template2, manager.GetTemplate(2));
        Assert.Equal(template3, manager.GetTemplate(3));
    }

    #endregion

    #region GetItemByItemId Tests

    [Fact]
    public void GetItemByItemId_ItemExists_ReturnsItem()
    {
        // Arrange
        var manager = CreateItemManager();
        var item = CreateTestItem(1000, 100, 5);
        var allItems = new Dictionary<ulong, Item> { { 1000, item } };
        SetPrivateField(manager, "_allItems", allItems);

        // Act
        var result = manager.GetItemByItemId(1000);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1000ul, result.Id);
        Assert.Equal(100u, result.TemplateId);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void GetItemByItemId_ItemDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var allItems = new Dictionary<ulong, Item>();
        SetPrivateField(manager, "_allItems", allItems);

        // Act
        var result = manager.GetItemByItemId(9999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetItemByItemId_ZeroId_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var allItems = new Dictionary<ulong, Item>();
        SetPrivateField(manager, "_allItems", allItems);

        // Act
        var result = manager.GetItemByItemId(0);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetItemByItemId_MultipleItems_ReturnsCorrectItem()
    {
        // Arrange
        var manager = CreateItemManager();
        var item1 = CreateTestItem(100, 1, 1);
        var item2 = CreateTestItem(200, 2, 2);
        var item3 = CreateTestItem(300, 3, 3);

        var allItems = new Dictionary<ulong, Item>
        {
            { 100, item1 },
            { 200, item2 },
            { 300, item3 }
        };
        SetPrivateField(manager, "_allItems", allItems);

        // Act & Assert
        Assert.Equal(item1, manager.GetItemByItemId(100));
        Assert.Equal(item2, manager.GetItemByItemId(200));
        Assert.Equal(item3, manager.GetItemByItemId(300));
    }

    #endregion

    #region AddItem Tests

    [Fact]
    public void AddItem_ValidItem_AddsToCollection()
    {
        // Arrange
        var manager = CreateItemManager();
        var item = CreateTestItem(2000, 100, 1);
        var allItems = new Dictionary<ulong, Item>();
        SetPrivateField(manager, "_allItems", allItems);

        // Act
        var result = manager.AddItem(item);

        // Assert
        Assert.True(result);
        Assert.Equal(item, manager.GetItemByItemId(2000));
    }

    [Fact]
    public void AddItem_DuplicateItem_ReturnsFalse()
    {
        // Arrange
        var manager = CreateItemManager();
        var item1 = CreateTestItem(2001, 100, 1);
        var item2 = CreateTestItem(2001, 100, 2);
        var allItems = new Dictionary<ulong, Item> { { 2001, item1 } };
        SetPrivateField(manager, "_allItems", allItems);

        // Act
        var result = manager.AddItem(item2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void AddItem_NullItem_ThrowsArgumentNullException()
    {
        // Arrange
        var manager = CreateItemManager();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => manager.AddItem(null));
    }

    #endregion

    #region GetAllItems Tests

    [Fact]
    public void GetAllItems_HasTemplates_ReturnsAllTemplates()
    {
        // Arrange
        var manager = CreateItemManager();
        var template1 = new ItemTemplate { Id = 1, Name = "Item 1" };
        var template2 = new ItemTemplate { Id = 2, Name = "Item 2" };
        var template3 = new ItemTemplate { Id = 3, Name = "Item 3" };

        var templates = new Dictionary<uint, ItemTemplate>
        {
            { 1, template1 },
            { 2, template2 },
            { 3, template3 }
        };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetAllItems();

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains(template1, result);
        Assert.Contains(template2, result);
        Assert.Contains(template3, result);
    }

    [Fact]
    public void GetAllItems_NoTemplates_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateItemManager();
        var templates = new Dictionary<uint, ItemTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetAllItems();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region GetGradeTemplate Tests

    [Fact]
    public void GetGradeTemplate_GradeExists_ReturnsTemplate()
    {
        // Arrange
        var manager = CreateItemManager();
        var grade = new GradeTemplate
        {
            Grade = 5,
            HoldableDps = 100,
            StatMultiplier = 2
        };
        var grades = new Dictionary<int, GradeTemplate> { { 5, grade } };
        SetPrivateField(manager, "_grades", grades);

        // Act
        var result = manager.GetGradeTemplate(5);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.Grade);
        Assert.Equal(100, result.HoldableDps);
        Assert.Equal(2, result.StatMultiplier);
    }

    [Fact]
    public void GetGradeTemplate_GradeDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var grades = new Dictionary<int, GradeTemplate>();
        SetPrivateField(manager, "_grades", grades);

        // Act
        var result = manager.GetGradeTemplate(99);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetArmorGradeBuff Tests

    [Fact]
    public void GetArmorGradeBuff_BuffExists_ReturnsBuff()
    {
        // Arrange
        var manager = CreateItemManager();
        var buff = new ArmorGradeBuff
        {
            Id = 1,
            ArmorType = ArmorType.Cloth,
            ItemGrade = ItemGrade.Heroic,
            BuffId = 1000
        };
        var armorGradeBuffs = new Dictionary<ArmorType, Dictionary<ItemGrade, ArmorGradeBuff>>
        {
            {
                ArmorType.Cloth,
                new Dictionary<ItemGrade, ArmorGradeBuff>
                {
                    { ItemGrade.Heroic, buff }
                }
            }
        };
        SetPrivateField(manager, "_armorGradeBuffs", armorGradeBuffs);

        // Act
        var result = manager.GetArmorGradeBuff(ArmorType.Cloth, ItemGrade.Heroic);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
        Assert.Equal(1000u, result.BuffId);
    }

    [Fact]
    public void GetArmorGradeBuff_ArmorTypeDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var armorGradeBuffs = new Dictionary<ArmorType, Dictionary<ItemGrade, ArmorGradeBuff>>();
        SetPrivateField(manager, "_armorGradeBuffs", armorGradeBuffs);

        // Act
        var result = manager.GetArmorGradeBuff(ArmorType.Cloth, ItemGrade.Heroic);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetArmorGradeBuff_GradeDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var armorGradeBuffs = new Dictionary<ArmorType, Dictionary<ItemGrade, ArmorGradeBuff>>
        {
            { ArmorType.Cloth, new Dictionary<ItemGrade, ArmorGradeBuff>() }
        };
        SetPrivateField(manager, "_armorGradeBuffs", armorGradeBuffs);

        // Act
        var result = manager.GetArmorGradeBuff(ArmorType.Cloth, ItemGrade.Heroic);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetUnitModifiers Tests

    [Fact]
    public void GetUnitModifiers_ModifiersExist_ReturnsModifiers()
    {
        // Arrange
        var manager = CreateItemManager();
        var modifiers = new List<BonusTemplate>
        {
            new() { Attribute = UnitAttribute.Str, Value = 10, ModifierType = UnitModifierType.Percent },
            new() { Attribute = UnitAttribute.Dex, Value = 5, ModifierType = UnitModifierType.Value }
        };
        var itemUnitModifiers = new Dictionary<uint, List<BonusTemplate>>
        {
            { 100, modifiers }
        };
        SetPrivateField(manager, "_itemUnitModifiers", itemUnitModifiers);

        // Act
        var result = manager.GetUnitModifiers(100);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(10, result[0].Value);
        Assert.Equal(UnitAttribute.Str, result[0].Attribute);
    }

    [Fact]
    public void GetUnitModifiers_NoModifiers_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateItemManager();
        var itemUnitModifiers = new Dictionary<uint, List<BonusTemplate>>();
        SetPrivateField(manager, "_itemUnitModifiers", itemUnitModifiers);

        // Act
        var result = manager.GetUnitModifiers(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetUnitModifiers_EmptyModifiers_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateItemManager();
        var itemUnitModifiers = new Dictionary<uint, List<BonusTemplate>>
        {
            { 100, new List<BonusTemplate>() }
        };
        SetPrivateField(manager, "_itemUnitModifiers", itemUnitModifiers);

        // Act
        var result = manager.GetUnitModifiers(100);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region GetItemProcTemplate Tests

    [Fact]
    public void GetItemProcTemplate_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var manager = CreateItemManager();
        var procTemplate = new ItemProcTemplate
        {
            Id = 50,
            SkillId = 100,
            ChanceRate = 50,
            CooldownSec = 30
        };
        var procTemplates = new Dictionary<uint, ItemProcTemplate> { { 50, procTemplate } };
        SetPrivateField(manager, "_itemProcTemplates", procTemplates);

        // Act
        var result = manager.GetItemProcTemplate(50);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(50u, result.Id);
        Assert.Equal(100u, result.SkillId);
        Assert.Equal(50u, result.ChanceRate);
        Assert.Equal(30u, result.CooldownSec);
    }

    [Fact]
    public void GetItemProcTemplate_TemplateDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var procTemplates = new Dictionary<uint, ItemProcTemplate>();
        SetPrivateField(manager, "_itemProcTemplates", procTemplates);

        // Act
        var result = manager.GetItemProcTemplate(999);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetEquippedItemSet Tests

    [Fact]
    public void GetEquippedItemSet_SetExists_ReturnsSet()
    {
        // Arrange
        var manager = CreateItemManager();
        var equipSet = new EquipItemSet
        {
            Id = 10
        };
        equipSet.Bonuses.Add(new EquipItemSetBonus { NumPieces = 3, BuffId = 100 });

        var equipItemSets = new Dictionary<uint, EquipItemSet> { { 10, equipSet } };
        SetPrivateField(manager, "_equipItemSets", equipItemSets);

        // Act
        var result = manager.GetEquippedItemSet(10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10u, result.Id);
        Assert.Single(result.Bonuses);
        Assert.Equal(3, result.Bonuses.First().NumPieces);
    }

    [Fact]
    public void GetEquippedItemSet_SetDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var equipItemSets = new Dictionary<uint, EquipItemSet>();
        SetPrivateField(manager, "_equipItemSets", equipItemSets);

        // Act
        var result = manager.GetEquippedItemSet(999);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetItemSet Tests

    [Fact]
    public void GetItemSet_SetExists_ReturnsSet()
    {
        // Arrange
        var manager = CreateItemManager();
        var itemSet = new ItemSet
        {
            Id = 20,
            Name = "Test Set",
            KindId = 1
        };
        var itemSets = new Dictionary<uint, ItemSet> { { 20, itemSet } };
        SetPrivateField(manager, "_itemSets", itemSets);

        // Act
        var result = manager.GetItemSet(20);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(20u, result.Id);
        Assert.Equal("Test Set", result.Name);
    }

    [Fact]
    public void GetItemSet_SetDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var itemSets = new Dictionary<uint, ItemSet>();
        SetPrivateField(manager, "_itemSets", itemSets);

        // Act
        var result = manager.GetItemSet(999);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetSocketChance Tests

    [Fact]
    public void GetSocketChance_ChanceExists_ReturnsChance()
    {
        // Arrange
        var manager = CreateItemManager();
        var socketChance = new Dictionary<uint, uint>
        {
            { 1, 100 },
            { 2, 80 },
            { 3, 60 }
        };
        SetPrivateField(manager, "_socketChance", socketChance);

        // Act
        var result = manager.GetSocketChance(0); // 0 + 1 = 1

        // Assert
        Assert.Equal(100u, result);
    }

    [Fact]
    public void GetSocketChance_ChanceDoesNotExist_ReturnsZero()
    {
        // Arrange
        var manager = CreateItemManager();
        var socketChance = new Dictionary<uint, uint>();
        SetPrivateField(manager, "_socketChance", socketChance);

        // Act
        var result = manager.GetSocketChance(10);

        // Assert
        Assert.Equal(0u, result);
    }

    #endregion

    #region GetItemIdsFromDoodad Tests

    [Fact]
    public void GetItemIdsFromDoodad_DoodadExists_ReturnsItemIds()
    {
        // Arrange
        var manager = CreateItemManager();
        var itemDoodadTemplate = new ItemDoodadTemplate
        {
            DoodadId = 100,
            ItemIds = new List<uint> { 1, 2, 3 }
        };
        var itemDoodadTemplates = new Dictionary<uint, ItemDoodadTemplate>
        {
            { 100, itemDoodadTemplate }
        };
        SetPrivateField(manager, "_itemDoodadTemplates", itemDoodadTemplates);

        // Act
        var result = manager.GetItemIdsFromDoodad(100);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Contains(1u, result);
        Assert.Contains(2u, result);
        Assert.Contains(3u, result);
    }

    [Fact]
    public void GetItemIdsFromDoodad_DoodadDoesNotExist_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateItemManager();
        var itemDoodadTemplates = new Dictionary<uint, ItemDoodadTemplate>();
        SetPrivateField(manager, "_itemDoodadTemplates", itemDoodadTemplates);

        // Act
        var result = manager.GetItemIdsFromDoodad(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region GetGradeDistributions Tests

    [Fact]
    public void GetGradeDistributions_DistributionExists_ReturnsDistribution()
    {
        // Arrange
        var manager = CreateItemManager();
        var distribution = new GradeDistributions
        {
            Id = 1,
            Name = "Common",
            Weight0 = 50,
            Weight1 = 30,
            Weight2 = 20
        };
        var distributions = new Dictionary<int, GradeDistributions> { { 1, distribution } };
        SetPrivateField(manager, "_itemGradeDistributions", distributions);

        // Act
        var result = manager.GetGradeDistributions(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("Common", result.Name);
        Assert.Equal(50, result.Weight0);
    }

    [Fact]
    public void GetGradeDistributions_DistributionDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var distributions = new Dictionary<int, GradeDistributions>();
        SetPrivateField(manager, "_itemGradeDistributions", distributions);

        // Act
        var result = manager.GetGradeDistributions(99);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetItemTemplateFromItemId Tests

    [Fact]
    public void GetItemTemplateFromItemId_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var manager = CreateItemManager();
        var template = new ItemTemplate { Id = 100, Name = "Test Item" };
        var templates = new Dictionary<uint, ItemTemplate> { { 100, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetItemTemplateFromItemId(100);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100u, result.Id);
    }

    [Fact]
    public void GetItemTemplateFromItemId_TemplateDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var templates = new Dictionary<uint, ItemTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetItemTemplateFromItemId(999);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetItemContainerByDbId Tests

    [Fact]
    public void GetItemContainerByDbId_ContainerDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var allPersistentContainers = new Dictionary<ulong, ItemContainer>();
        SetPrivateField(manager, "_allPersistentContainers", allPersistentContainers);

        // Act
        var result = manager.GetItemContainerByDbId(999);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region ReleaseId Tests

    [Fact]
    public void ReleaseId_ValidId_RemovesFromItems()
    {
        // Arrange
        var mockItemId = new Mock<IItemIdManager>();
        var manager = CreateItemManager(mockItemId: mockItemId);
        var item = CreateTestItem(3000, 100, 1);
        var allItems = new Dictionary<ulong, Item> { { 3000, item } };
        var removedItems = new List<ulong>();
        SetPrivateField(manager, "_allItems", allItems);
        SetPrivateField(manager, "_removedItems", removedItems);

        // Act
        manager.ReleaseId(3000);

        // Assert
        Assert.Null(manager.GetItemByItemId(3000));
        Assert.Contains(3000ul, removedItems);
        mockItemId.Verify(x => x.ReleaseId(3000), Times.Once);
    }

    [Fact]
    public void ReleaseId_ZeroId_DoesNotAddToRemoved()
    {
        // Arrange
        var mockItemId = new Mock<IItemIdManager>();
        var manager = CreateItemManager(mockItemId: mockItemId);
        var allItems = new Dictionary<ulong, Item>();
        var removedItems = new List<ulong>();
        SetPrivateField(manager, "_allItems", allItems);
        SetPrivateField(manager, "_removedItems", removedItems);

        // Act
        manager.ReleaseId(0);

        // Assert
        Assert.DoesNotContain(0ul, removedItems);
    }

    #endregion

    #region IsAutoEquipTradePack Tests

    [Fact]
    public void IsAutoEquipTradePack_ValidTradePack_ReturnsTrue()
    {
        // Arrange
        var manager = CreateItemManager();
        var template = new BackpackTemplate
        {
            Id = 100,
            Name = "Trade Pack",
            BindType = ItemBindType.BindOnPickup
        };
        var templates = new Dictionary<uint, ItemTemplate> { { 100, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.IsAutoEquipTradePack(100);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsAutoEquipTradePack_NotBackpack_ReturnsFalse()
    {
        // Arrange
        var manager = CreateItemManager();
        var template = new ItemTemplate
        {
            Id = 100,
            Name = "Regular Item",
            BindType = ItemBindType.Normal
        };
        var templates = new Dictionary<uint, ItemTemplate> { { 100, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.IsAutoEquipTradePack(100);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsAutoEquipTradePack_BindOnEquip_ReturnsFalse()
    {
        // Arrange
        var manager = CreateItemManager();
        var template = new BackpackTemplate
        {
            Id = 100,
            Name = "Trade Pack",
            BindType = ItemBindType.BindOnEquip
        };
        var templates = new Dictionary<uint, ItemTemplate> { { 100, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.IsAutoEquipTradePack(100);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsAutoEquipTradePack_InvalidTemplate_ReturnsFalse()
    {
        // Arrange
        var manager = CreateItemManager();
        var templates = new Dictionary<uint, ItemTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.IsAutoEquipTradePack(999);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetContainerSlotTypeByContainerId Tests

    [Fact]
    public void GetContainerSlotTypeByContainerId_ContainerDoesNotExist_ReturnsNone()
    {
        // Arrange
        var manager = CreateItemManager();
        var allPersistentContainers = new Dictionary<ulong, ItemContainer>();
        SetPrivateField(manager, "_allPersistentContainers", allPersistentContainers);

        // Act
        var result = manager.GetContainerSlotTypeByContainerId(999);

        // Assert
        Assert.Equal(SlotType.None, result);
    }

    #endregion

    #region Config Value Tests

    [Fact]
    public void GetDurabilityRepairCostFactor_ReturnsFactor()
    {
        // Arrange
        var manager = CreateItemManager();
        var config = new ItemConfig { DurabilityRepairCostFactor = 0.5f };
        SetPrivateField(manager, "_config", config);

        // Act
        var result = manager.GetDurabilityRepairCostFactor();

        // Assert
        Assert.Equal(0.5f, result);
    }

    [Fact]
    public void GetDurabilityConst_ReturnsConst()
    {
        // Arrange
        var manager = CreateItemManager();
        var config = new ItemConfig { DurabilityConst = 100f };
        SetPrivateField(manager, "_config", config);

        // Act
        var result = manager.GetDurabilityConst();

        // Assert
        Assert.Equal(100f, result);
    }

    [Fact]
    public void GetItemStatConst_ReturnsConst()
    {
        // Arrange
        var manager = CreateItemManager();
        var config = new ItemConfig { ItemStatConst = 50 };
        SetPrivateField(manager, "_config", config);

        // Act
        var result = manager.GetItemStatConst();

        // Assert
        Assert.Equal(50, result);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void GetTemplate_MaxUIntId_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var templates = new Dictionary<uint, ItemTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(uint.MaxValue);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetItemByItemId_MaxULongId_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var allItems = new Dictionary<ulong, Item>();
        SetPrivateField(manager, "_allItems", allItems);

        // Act
        var result = manager.GetItemByItemId(ulong.MaxValue);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void AddItem_MaxCountItem_AddsSuccessfully()
    {
        // Arrange
        var manager = CreateItemManager();
        var item = CreateTestItem(8000, 100, int.MaxValue);
        var allItems = new Dictionary<ulong, Item>();
        SetPrivateField(manager, "_allItems", allItems);

        // Act
        var result = manager.AddItem(item);

        // Assert
        Assert.True(result);
        Assert.Equal(int.MaxValue, manager.GetItemByItemId(8000).Count);
    }

    [Fact]
    public void GetAllItems_LargeCollection_ReturnsAllItems()
    {
        // Arrange
        var manager = CreateItemManager();
        var templates = new Dictionary<uint, ItemTemplate>();
        for (uint i = 1; i <= 1000; i++)
        {
            templates.Add(i, new ItemTemplate { Id = i, Name = $"Item {i}" });
        }
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetAllItems();

        // Assert
        Assert.Equal(1000, result.Count);
    }

    #endregion

    #region Helper Methods

    private static ItemManager CreateItemManager(
        Mock<ISkillManager> mockSkill = null,
        Mock<IItemIdManager> mockItemId = null,
        Mock<IContainerIdManager> mockContainerId = null,
        Mock<ILocalizationManager> mockLocale = null,
        Mock<ITaskManager> mockTask = null,
        Mock<IWorldManager> mockWorld = null)
    {
        return new ItemManager(
            (mockSkill ?? new Mock<ISkillManager>()).Object,
            (mockItemId ?? new Mock<IItemIdManager>()).Object,
            (mockContainerId ?? new Mock<IContainerIdManager>()).Object,
            (mockLocale ?? new Mock<ILocalizationManager>()).Object,
            (mockTask ?? new Mock<ITaskManager>()).Object,
            (mockWorld ?? new Mock<IWorldManager>()).Object);
    }

    private static void SetPrivateField(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(obj, value);
    }

    private static Item CreateTestItem(ulong id, uint templateId, int count)
    {
        var template = new ItemTemplate
        {
            Id = templateId,
            Name = $"Test Item {templateId}",
            MaxCount = 100,
            BindType = ItemBindType.Normal
        };

        return new Item(id, template, count);
    }

    #endregion
}
