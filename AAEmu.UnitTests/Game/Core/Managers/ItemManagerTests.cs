using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Procs;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class ItemManagerTests
{
    #region Constructor Tests

    [Test]
    public async Task Constructor_DoesNotCallDependencies()
    {
        // Arrange
        var mockSkill = Mock.Of<ISkillManager>();
        var mockItemId = Mock.Of<IItemIdManager>();
        var mockContainerId = Mock.Of<IContainerIdManager>();
        var mockLocale = Mock.Of<ILocalizationManager>();
        var mockTask = Mock.Of<ITaskManager>();
        var mockWorld = Mock.Of<IWorldManager>();

        // Act
        var manager = new ItemManager(
            mockSkill.Object,
            mockItemId.Object,
            mockContainerId.Object,
            mockLocale.Object,
            mockTask.Object,
            mockWorld.Object);

        // Assert
        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockSkill);
        Mock.VerifyNoOtherCalls(mockItemId);
        Mock.VerifyNoOtherCalls(mockContainerId);
        Mock.VerifyNoOtherCalls(mockLocale);
        Mock.VerifyNoOtherCalls(mockTask);
        Mock.VerifyNoOtherCalls(mockWorld);
    }

    #endregion

    #region GetTemplate Tests

    [Test]
    public async Task GetTemplate_TemplateExists_ReturnsTemplate()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(100u);
        await Assert.That(result.Name).IsEqualTo("Test Item");
        await Assert.That(result.Level).IsEqualTo(10);
        await Assert.That(result.Price).IsEqualTo(50);
    }

    [Test]
    public async Task GetTemplate_TemplateDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var templates = new Dictionary<uint, ItemTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetTemplate_ZeroId_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var templates = new Dictionary<uint, ItemTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(0);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetTemplate_MultipleTemplates_ReturnsCorrectTemplate()
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
        await Assert.That(manager.GetTemplate(1)).IsEqualTo(template1);
        await Assert.That(manager.GetTemplate(2)).IsEqualTo(template2);
        await Assert.That(manager.GetTemplate(3)).IsEqualTo(template3);
    }

    #endregion

    #region GetItemByItemId Tests

    [Test]
    public async Task GetItemByItemId_ItemExists_ReturnsItem()
    {
        // Arrange
        var manager = CreateItemManager();
        var item = CreateTestItem(1000, 100, 5);
        var allItems = new Dictionary<ulong, Item> { { 1000, item } };
        SetPrivateField(manager, "_allItems", allItems);

        // Act
        var result = manager.GetItemByItemId(1000);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1000ul);
        await Assert.That(result.TemplateId).IsEqualTo(100u);
        await Assert.That(result.Count).IsEqualTo(5);
    }

    [Test]
    public async Task GetItemByItemId_ItemDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var allItems = new Dictionary<ulong, Item>();
        SetPrivateField(manager, "_allItems", allItems);

        // Act
        var result = manager.GetItemByItemId(9999);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetItemByItemId_ZeroId_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var allItems = new Dictionary<ulong, Item>();
        SetPrivateField(manager, "_allItems", allItems);

        // Act
        var result = manager.GetItemByItemId(0);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetItemByItemId_MultipleItems_ReturnsCorrectItem()
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
        await Assert.That(manager.GetItemByItemId(100)).IsEqualTo(item1);
        await Assert.That(manager.GetItemByItemId(200)).IsEqualTo(item2);
        await Assert.That(manager.GetItemByItemId(300)).IsEqualTo(item3);
    }

    #endregion

    #region AddItem Tests

    [Test]
    public async Task AddItem_ValidItem_AddsToCollection()
    {
        // Arrange
        var manager = CreateItemManager();
        var item = CreateTestItem(2000, 100, 1);
        var allItems = new Dictionary<ulong, Item>();
        SetPrivateField(manager, "_allItems", allItems);

        // Act
        var result = manager.AddItem(item);

        // Assert
        await Assert.That(result).IsTrue();
        await Assert.That(manager.GetItemByItemId(2000)).IsEqualTo(item);
    }

    [Test]
    public async Task AddItem_DuplicateItem_ReturnsFalse()
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
        await Assert.That(result).IsFalse();
    }

    [Test]
    public void AddItem_NullItem_ThrowsArgumentNullException()
    {
        // Arrange
        var manager = CreateItemManager();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => manager.AddItem(null));
    }

    #endregion

    #region GetAllItems Tests

    [Test]
    public async Task GetAllItems_HasTemplates_ReturnsAllTemplates()
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
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result).Contains(template1);
        await Assert.That(result).Contains(template2);
        await Assert.That(result).Contains(template3);
    }

    [Test]
    public async Task GetAllItems_NoTemplates_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateItemManager();
        var templates = new Dictionary<uint, ItemTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetAllItems();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetGradeTemplate Tests

    [Test]
    public async Task GetGradeTemplate_GradeExists_ReturnsTemplate()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Grade).IsEqualTo(5);
        await Assert.That(result.HoldableDps).IsEqualTo(100);
        await Assert.That(result.StatMultiplier).IsEqualTo(2);
    }

    [Test]
    public async Task GetGradeTemplate_GradeDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var grades = new Dictionary<int, GradeTemplate>();
        SetPrivateField(manager, "_grades", grades);

        // Act
        var result = manager.GetGradeTemplate(99);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetArmorGradeBuff Tests

    [Test]
    public async Task GetArmorGradeBuff_BuffExists_ReturnsBuff()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
        await Assert.That(result.BuffId).IsEqualTo(1000u);
    }

    [Test]
    public async Task GetArmorGradeBuff_ArmorTypeDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var armorGradeBuffs = new Dictionary<ArmorType, Dictionary<ItemGrade, ArmorGradeBuff>>();
        SetPrivateField(manager, "_armorGradeBuffs", armorGradeBuffs);

        // Act
        var result = manager.GetArmorGradeBuff(ArmorType.Cloth, ItemGrade.Heroic);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetArmorGradeBuff_GradeDoesNotExist_ReturnsNull()
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
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetUnitModifiers Tests

    [Test]
    public async Task GetUnitModifiers_ModifiersExist_ReturnsModifiers()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0].Value).IsEqualTo(10);
        await Assert.That(result[0].Attribute).IsEqualTo(UnitAttribute.Str);
    }

    [Test]
    public async Task GetUnitModifiers_NoModifiers_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateItemManager();
        var itemUnitModifiers = new Dictionary<uint, List<BonusTemplate>>();
        SetPrivateField(manager, "_itemUnitModifiers", itemUnitModifiers);

        // Act
        var result = manager.GetUnitModifiers(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetUnitModifiers_EmptyModifiers_ReturnsEmptyList()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetItemProcTemplate Tests

    [Test]
    public async Task GetItemProcTemplate_TemplateExists_ReturnsTemplate()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(50u);
        await Assert.That(result.SkillId).IsEqualTo(100u);
        await Assert.That(result.ChanceRate).IsEqualTo(50u);
        await Assert.That(result.CooldownSec).IsEqualTo(30u);
    }

    [Test]
    public async Task GetItemProcTemplate_TemplateDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var procTemplates = new Dictionary<uint, ItemProcTemplate>();
        SetPrivateField(manager, "_itemProcTemplates", procTemplates);

        // Act
        var result = manager.GetItemProcTemplate(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetEquippedItemSet Tests

    [Test]
    public async Task GetEquippedItemSet_SetExists_ReturnsSet()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(10u);
        await Assert.That(result.Bonuses).HasSingleItem();
        await Assert.That(result.Bonuses.First().NumPieces).IsEqualTo(3);
    }

    [Test]
    public async Task GetEquippedItemSet_SetDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var equipItemSets = new Dictionary<uint, EquipItemSet>();
        SetPrivateField(manager, "_equipItemSets", equipItemSets);

        // Act
        var result = manager.GetEquippedItemSet(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetItemSet Tests

    [Test]
    public async Task GetItemSet_SetExists_ReturnsSet()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(20u);
        await Assert.That(result.Name).IsEqualTo("Test Set");
    }

    [Test]
    public async Task GetItemSet_SetDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var itemSets = new Dictionary<uint, ItemSet>();
        SetPrivateField(manager, "_itemSets", itemSets);

        // Act
        var result = manager.GetItemSet(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetSocketChance Tests

    [Test]
    public async Task GetSocketChance_ChanceExists_ReturnsChance()
    {
        // Arrange
        var manager = CreateItemManager();
        var socketChance = new Dictionary<uint, ItemSocketChance>
        {
            { 1, new ItemSocketChance { Id = 1, CostRatio = 100 } },
            { 2, new ItemSocketChance { Id = 2, CostRatio = 80 } },
            { 3, new ItemSocketChance { Id = 3, CostRatio = 60 } }
        };
        SetPrivateField(manager, "_socketChance", socketChance);

        // Act
        var result = manager.GetSocketChance(0); // 0 + 1 = 1

        // Assert
        await Assert.That(result).IsEqualTo(100u);
    }

    [Test]
    public async Task GetSocketChance_ChanceDoesNotExist_ReturnsZero()
    {
        // Arrange
        var manager = CreateItemManager();
        var socketChance = new Dictionary<uint, ItemSocketChance>();
        SetPrivateField(manager, "_socketChance", socketChance);

        // Act
        var result = manager.GetSocketChance(10);

        // Assert
        await Assert.That(result).IsEqualTo(0u);
    }

    #endregion

    #region GetItemIdsFromDoodad Tests

    [Test]
    public async Task GetItemIdsFromDoodad_DoodadExists_ReturnsItemIds()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result).Contains(1u);
        await Assert.That(result).Contains(2u);
        await Assert.That(result).Contains(3u);
    }

    [Test]
    public async Task GetItemIdsFromDoodad_DoodadDoesNotExist_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateItemManager();
        var itemDoodadTemplates = new Dictionary<uint, ItemDoodadTemplate>();
        SetPrivateField(manager, "_itemDoodadTemplates", itemDoodadTemplates);

        // Act
        var result = manager.GetItemIdsFromDoodad(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetGradeDistributions Tests

    [Test]
    public async Task GetGradeDistributions_DistributionExists_ReturnsDistribution()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1);
        await Assert.That(result.Name).IsEqualTo("Common");
        await Assert.That(result.Weight0).IsEqualTo(50);
    }

    [Test]
    public async Task GetGradeDistributions_DistributionDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var distributions = new Dictionary<int, GradeDistributions>();
        SetPrivateField(manager, "_itemGradeDistributions", distributions);

        // Act
        var result = manager.GetGradeDistributions(99);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetItemTemplateFromItemId Tests

    [Test]
    public async Task GetItemTemplateFromItemId_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var manager = CreateItemManager();
        var template = new ItemTemplate { Id = 100, Name = "Test Item" };
        var templates = new Dictionary<uint, ItemTemplate> { { 100, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetItemTemplateFromItemId(100);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(100u);
    }

    [Test]
    public async Task GetItemTemplateFromItemId_TemplateDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var templates = new Dictionary<uint, ItemTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetItemTemplateFromItemId(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetItemContainerByDbId Tests

    [Test]
    public async Task GetItemContainerByDbId_ContainerDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var allPersistentContainers = new Dictionary<ulong, ItemContainer>();
        SetPrivateField(manager, "_allPersistentContainers", allPersistentContainers);

        // Act
        var result = manager.GetItemContainerByDbId(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region ReleaseId Tests

    [Test]
    public async Task ReleaseId_ValidId_RemovesFromItems()
    {
        // Arrange
        var mockItemId = Mock.Of<IItemIdManager>();
        var manager = CreateItemManager(mockItemId: mockItemId);
        var item = CreateTestItem(3000, 100, 1);
        var allItems = new Dictionary<ulong, Item> { { 3000, item } };
        var removedItems = new List<ulong>();
        SetPrivateField(manager, "_allItems", allItems);
        SetPrivateField(manager, "_removedItems", removedItems);

        // Act
        manager.ReleaseId(3000);

        // Assert
        await Assert.That(manager.GetItemByItemId(3000)).IsNull();
        await Assert.That(removedItems).Contains(3000ul);
        mockItemId.ReleaseId(3000).WasCalled(Times.Once);
    }

    [Test]
    public async Task ReleaseId_ZeroId_DoesNotAddToRemoved()
    {
        // Arrange
        var mockItemId = Mock.Of<IItemIdManager>();
        var manager = CreateItemManager(mockItemId: mockItemId);
        var allItems = new Dictionary<ulong, Item>();
        var removedItems = new List<ulong>();
        SetPrivateField(manager, "_allItems", allItems);
        SetPrivateField(manager, "_removedItems", removedItems);

        // Act
        manager.ReleaseId(0);

        // Assert
        await Assert.That(removedItems).DoesNotContain(0ul);
    }

    #endregion

    #region IsAutoEquipTradePack Tests

    [Test]
    public async Task IsAutoEquipTradePack_ValidTradePack_ReturnsTrue()
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
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsAutoEquipTradePack_NotBackpack_ReturnsFalse()
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
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsAutoEquipTradePack_BindOnEquip_ReturnsFalse()
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
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsAutoEquipTradePack_InvalidTemplate_ReturnsFalse()
    {
        // Arrange
        var manager = CreateItemManager();
        var templates = new Dictionary<uint, ItemTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.IsAutoEquipTradePack(999);

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    #region GetContainerSlotTypeByContainerId Tests

    [Test]
    public async Task GetContainerSlotTypeByContainerId_ContainerDoesNotExist_ReturnsNone()
    {
        // Arrange
        var manager = CreateItemManager();
        var allPersistentContainers = new Dictionary<ulong, ItemContainer>();
        SetPrivateField(manager, "_allPersistentContainers", allPersistentContainers);

        // Act
        var result = manager.GetContainerSlotTypeByContainerId(999);

        // Assert
        await Assert.That(result).IsEqualTo(SlotType.None);
    }

    #endregion

    #region Config Value Tests

    [Test]
    public async Task GetDurabilityRepairCostFactor_ReturnsFactor()
    {
        // Arrange
        var manager = CreateItemManager();
        var config = new ItemConfig { DurabilityRepairCostFactor = 0.5f };
        SetPrivateField(manager, "_config", config);

        // Act
        var result = manager.GetDurabilityRepairCostFactor();

        // Assert
        await Assert.That(result).IsEqualTo(0.5f);
    }

    [Test]
    public async Task GetDurabilityConst_ReturnsConst()
    {
        // Arrange
        var manager = CreateItemManager();
        var config = new ItemConfig { DurabilityConst = 100f };
        SetPrivateField(manager, "_config", config);

        // Act
        var result = manager.GetDurabilityConst();

        // Assert
        await Assert.That(result).IsEqualTo(100f);
    }

    [Test]
    public async Task GetItemStatConst_ReturnsConst()
    {
        // Arrange
        var manager = CreateItemManager();
        var config = new ItemConfig { ItemStatConst = 50 };
        SetPrivateField(manager, "_config", config);

        // Act
        var result = manager.GetItemStatConst();

        // Assert
        await Assert.That(result).IsEqualTo(50);
    }

    #endregion

    #region Edge Case Tests

    [Test]
    public async Task GetTemplate_MaxUIntId_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var templates = new Dictionary<uint, ItemTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(uint.MaxValue);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetItemByItemId_MaxULongId_ReturnsNull()
    {
        // Arrange
        var manager = CreateItemManager();
        var allItems = new Dictionary<ulong, Item>();
        SetPrivateField(manager, "_allItems", allItems);

        // Act
        var result = manager.GetItemByItemId(ulong.MaxValue);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task AddItem_MaxCountItem_AddsSuccessfully()
    {
        // Arrange
        var manager = CreateItemManager();
        var item = CreateTestItem(8000, 100, int.MaxValue);
        var allItems = new Dictionary<ulong, Item>();
        SetPrivateField(manager, "_allItems", allItems);

        // Act
        var result = manager.AddItem(item);

        // Assert
        await Assert.That(result).IsTrue();
        await Assert.That(manager.GetItemByItemId(8000).Count).IsEqualTo(int.MaxValue);
    }

    [Test]
    public async Task GetAllItems_LargeCollection_ReturnsAllItems()
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
        await Assert.That(result.Count).IsEqualTo(1000);
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
            (mockSkill ?? Mock.Of<ISkillManager>()).Object,
            (mockItemId ?? Mock.Of<IItemIdManager>()).Object,
            (mockContainerId ?? Mock.Of<IContainerIdManager>()).Object,
            (mockLocale ?? Mock.Of<ILocalizationManager>()).Object,
            (mockTask ?? Mock.Of<ITaskManager>()).Object,
            (mockWorld ?? Mock.Of<IWorldManager>()).Object);
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