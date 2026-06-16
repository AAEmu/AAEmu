using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Details;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Managers.UnitManagers;

public class DoodadManagerTests
{
    #region Constructor Tests

    [Test]
    public async Task Constructor_DoesNotCallDeps()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();

        // Act
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        // Assert
        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockObjId);
        Mock.VerifyNoOtherCalls(mockDoodadId);
        Mock.VerifyNoOtherCalls(mockItem);
        Mock.VerifyNoOtherCalls(mockHousing);
        Mock.VerifyNoOtherCalls(mockSus);
    }

    #endregion

    #region Exist Tests

    [Test]
    public async Task Exist_TemplateExists_ReturnsTrue()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var template = new DoodadTemplate { Id = 1 };
        var templates = new Dictionary<uint, DoodadTemplate> { { 1, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.Exist(1);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Exist_TemplateDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.Exist(999);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task Exist_ZeroId_ReturnsFalse()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.Exist(0);

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    #region GetTemplate Tests

    [Test]
    public async Task GetTemplate_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var template = new DoodadTemplate { Id = 1, GroupId = 100 };
        var templates = new Dictionary<uint, DoodadTemplate> { { 1, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
        await Assert.That(result.GroupId).IsEqualTo(100u);
    }

    [Test]
    public async Task GetTemplate_TemplateDoesNotExist_ReturnsNull()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
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
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(0);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region Create Tests

    // Note: Create tests that involve WorldInstance require WorldManager singleton initialization,
    // which makes them integration tests rather than unit tests. These tests are omitted here
    // but would be covered in integration testing.

    [Test]
    public async Task Create_WithInvalidTemplate_ReturnsNull()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        var mockWorld = Mock.Of<WorldInstance>(new WorldTemplate { Id = 1, Name = "TestWorld" }, 1u, false, 1u);

        // Act
        var result = manager.Create(mockWorld.Object, 0, 999, null, true);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Create_WithNullWorld_ReturnsNull()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var template = new DoodadTemplate { Id = 1 };
        template.FuncGroups.Add(new DoodadFuncGroups { Id = 10, Almighty = 1, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Start });
        var templates = new Dictionary<uint, DoodadTemplate> { { 1, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.Create(null, 0, 1, null, true);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetFunc Tests

    [Test]
    public async Task GetFunc_ByFuncId_ReturnsFunc()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var func = new DoodadFunc { FuncKey = 1, GroupId = 10, FuncId = 100 };
        var funcsById = new Dictionary<uint, DoodadFunc> { { 1, func } };
        SetPrivateField(manager, "_funcsById", funcsById);

        // Act
        var result = manager.GetFunc(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.FuncKey).IsEqualTo(1u);
        await Assert.That(result.GroupId).IsEqualTo(10u);
    }

    [Test]
    public async Task GetFunc_ByFuncId_NotFound_ReturnsNull()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcsById = new Dictionary<uint, DoodadFunc>();
        SetPrivateField(manager, "_funcsById", funcsById);

        // Act
        var result = manager.GetFunc(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetFunc_ByGroupIdAndSkillId_ReturnsFunc()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var func = new DoodadFunc { FuncKey = 1, GroupId = 10, SkillId = 50 };
        var funcsByGroups = new Dictionary<uint, List<DoodadFunc>>
        {
            { 10, new List<DoodadFunc> { func } }
        };
        SetPrivateField(manager, "_funcsByGroups", funcsByGroups);

        // Act
        var result = manager.GetFunc(10, 50);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.SkillId).IsEqualTo(50u);
    }

    [Test]
    public async Task GetFunc_ByGroupId_GroupNotFound_ReturnsNull()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcsByGroups = new Dictionary<uint, List<DoodadFunc>>();
        SetPrivateField(manager, "_funcsByGroups", funcsByGroups);

        // Act
        var result = manager.GetFunc(999, 0);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetFuncsForGroup Tests

    [Test]
    public async Task GetFuncsForGroup_GroupExists_ReturnsFuncs()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcs = new List<DoodadFunc>
        {
            new() { FuncKey = 1, GroupId = 10 },
            new() { FuncKey = 2, GroupId = 10 }
        };
        var funcsByGroups = new Dictionary<uint, List<DoodadFunc>> { { 10, funcs } };
        SetPrivateField(manager, "_funcsByGroups", funcsByGroups);

        // Act
        var result = manager.GetFuncsForGroup(10);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetFuncsForGroup_GroupNotFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcsByGroups = new Dictionary<uint, List<DoodadFunc>>();
        SetPrivateField(manager, "_funcsByGroups", funcsByGroups);

        // Act
        var result = manager.GetFuncsForGroup(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetPhaseFunc Tests

    [Test]
    public async Task GetPhaseFunc_GroupExists_ReturnsPhaseFuncs()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var phaseFuncs = new List<DoodadPhaseFunc>
        {
            new() { GroupId = 10, FuncId = 1 },
            new() { GroupId = 10, FuncId = 2 }
        };
        var phaseFuncsByGroups = new Dictionary<uint, List<DoodadPhaseFunc>> { { 10, phaseFuncs } };
        SetPrivateField(manager, "_phaseFuncs", phaseFuncsByGroups);

        // Act
        var result = manager.GetPhaseFunc(10);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetPhaseFunc_GroupNotFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var phaseFuncsByGroups = new Dictionary<uint, List<DoodadPhaseFunc>>();
        SetPrivateField(manager, "_phaseFuncs", phaseFuncsByGroups);

        // Act
        var result = manager.GetPhaseFunc(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetDoodadFuncGroups Tests

    [Test]
    public async Task GetDoodadFuncGroups_TemplateExists_ReturnsGroups()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var template = new DoodadTemplate { Id = 1 };
        template.FuncGroups.Add(new DoodadFuncGroups { Id = 10, Almighty = 1, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Start });
        template.FuncGroups.Add(new DoodadFuncGroups { Id = 20, Almighty = 1, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal });
        var templates = new Dictionary<uint, DoodadTemplate> { { 1, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetDoodadFuncGroups(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetDoodadFuncGroups_TemplateNotFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetDoodadFuncGroups(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetDoodadFuncGroupsId Tests

    [Test]
    public async Task GetDoodadFuncGroupsId_TemplateExists_ReturnsGroupIds()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var template = new DoodadTemplate { Id = 1 };
        template.FuncGroups.Add(new DoodadFuncGroups { Id = 10, Almighty = 1, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Start });
        template.FuncGroups.Add(new DoodadFuncGroups { Id = 20, Almighty = 1, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal });
        var templates = new Dictionary<uint, DoodadTemplate> { { 1, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetDoodadFuncGroupsId(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result).Contains(10u);
        await Assert.That(result).Contains(20u);
    }

    [Test]
    public async Task GetDoodadFuncGroupsId_TemplateNotFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetDoodadFuncGroupsId(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetDoodadFuncConsumeChangerItemList Tests

    [Test]
    public async Task GetDoodadFuncConsumeChangerItemList_ItemsExist_ReturnsItemIds()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var consumeItems = new Dictionary<uint, DoodadFuncConsumeChangerItem>
        {
            { 1, new DoodadFuncConsumeChangerItem { Id = 1, DoodadFuncConsumeChangerId = 10, ItemId = 100 } },
            { 2, new DoodadFuncConsumeChangerItem { Id = 2, DoodadFuncConsumeChangerId = 10, ItemId = 200 } },
            { 3, new DoodadFuncConsumeChangerItem { Id = 3, DoodadFuncConsumeChangerId = 20, ItemId = 300 } }
        };
        SetPrivateField(manager, "_doodadFuncConsumeChangerItem", consumeItems);

        // Act
        var result = manager.GetDoodadFuncConsumeChangerItemList(10);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result).Contains(100u);
        await Assert.That(result).Contains(200u);
    }

    [Test]
    public async Task GetDoodadFuncConsumeChangerItemList_NoItemsFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var consumeItems = new Dictionary<uint, DoodadFuncConsumeChangerItem>();
        SetPrivateField(manager, "_doodadFuncConsumeChangerItem", consumeItems);

        // Act
        var result = manager.GetDoodadFuncConsumeChangerItemList(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetTreasureChestTemplateIds Tests

    [Test]
    public async Task GetTreasureChestTemplateIds_ReturnsChestTemplates()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>
        {
            { 1, new DoodadTemplate { Id = 1, GroupId = 55 } },  // Treasure chest group
            { 2, new DoodadTemplate { Id = 2, GroupId = 56 } },  // Treasure chest group
            { 3, new DoodadTemplate { Id = 3, GroupId = 60 } },  // Not a treasure chest
            { 4, new DoodadTemplate { Id = 4, GroupId = 57 } },  // Treasure chest group
            { 5, new DoodadTemplate { Id = 5, GroupId = 54 } }   // Not a treasure chest
        };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTreasureChestTemplateIds();

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result).Contains(1u);
        await Assert.That(result).Contains(2u);
        await Assert.That(result).Contains(4u);
        await Assert.That(result).DoesNotContain(3u);
        await Assert.That(result).DoesNotContain(5u);
    }

    [Test]
    public async Task GetTreasureChestTemplateIds_NoTemplates_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTreasureChestTemplateIds();

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetDoodadPhaseFuncs Tests

    [Test]
    public async Task GetDoodadPhaseFuncs_GroupExists_ReturnsPhaseFuncs()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var phaseFuncs = new List<DoodadPhaseFunc>
        {
            new() { GroupId = 10, FuncId = 1, FuncType = "DoodadFuncTimer" },
            new() { GroupId = 10, FuncId = 2, FuncType = "DoodadFuncGrowth" }
        };
        var phaseFuncsDict = new Dictionary<uint, List<DoodadPhaseFunc>> { { 10, phaseFuncs } };
        SetPrivateField(manager, "_phaseFuncs", phaseFuncsDict);

        // Act
        var result = manager.GetDoodadPhaseFuncs(10);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetDoodadPhaseFuncs_GroupNotFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var phaseFuncsDict = new Dictionary<uint, List<DoodadPhaseFunc>>();
        SetPrivateField(manager, "_phaseFuncs", phaseFuncsDict);

        // Act
        var result = manager.GetDoodadPhaseFuncs(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetFuncTemplate Tests

    [Test]
    public async Task GetFuncTemplate_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcTemplate = new DoodadFuncUse { Id = 1, SkillId = 100 };
        var funcTemplates = new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>
        {
            { "DoodadFuncUse", new Dictionary<uint, DoodadFuncTemplate> { { 1, funcTemplate } } }
        };
        SetPrivateField(manager, "_funcTemplates", funcTemplates);

        // Act
        var result = manager.GetFuncTemplate(1, "DoodadFuncUse");

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
    }

    [Test]
    public async Task GetFuncTemplate_TypeNotFound_ReturnsNull()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcTemplates = new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>();
        SetPrivateField(manager, "_funcTemplates", funcTemplates);

        // Act
        var result = manager.GetFuncTemplate(1, "NonExistentType");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetFuncTemplate_TemplateNotFound_ReturnsNull()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcTemplates = new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>
        {
            { "DoodadFuncUse", new Dictionary<uint, DoodadFuncTemplate>() }
        };
        SetPrivateField(manager, "_funcTemplates", funcTemplates);

        // Act
        var result = manager.GetFuncTemplate(999, "DoodadFuncUse");

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetPhaseFuncTemplate Tests

    [Test]
    public async Task GetPhaseFuncTemplate_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var phaseFuncTemplate = new DoodadFuncTimer { Id = 1, Delay = 5000 };
        var phaseFuncTemplates = new Dictionary<string, Dictionary<uint, DoodadPhaseFuncTemplate>>
        {
            { "DoodadFuncTimer", new Dictionary<uint, DoodadPhaseFuncTemplate> { { 1, phaseFuncTemplate } } }
        };
        SetPrivateField(manager, "_phaseFuncTemplates", phaseFuncTemplates);

        // Act
        var result = manager.GetPhaseFuncTemplate(1, "DoodadFuncTimer");

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
    }

    [Test]
    public async Task GetPhaseFuncTemplate_TypeNotFound_ReturnsNull()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var phaseFuncTemplates = new Dictionary<string, Dictionary<uint, DoodadPhaseFuncTemplate>>();
        SetPrivateField(manager, "_phaseFuncTemplates", phaseFuncTemplates);

        // Act
        var result = manager.GetPhaseFuncTemplate(1, "NonExistentType");

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetDoodadFuncs Tests

    [Test]
    public async Task GetDoodadFuncs_GroupExists_ReturnsFuncs()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcs = new List<DoodadFunc>
        {
            new() { FuncKey = 1, GroupId = 10, FuncType = "DoodadFuncUse" },
            new() { FuncKey = 2, GroupId = 10, FuncType = "DoodadFuncLootItem" }
        };
        var funcsByGroups = new Dictionary<uint, List<DoodadFunc>> { { 10, funcs } };
        SetPrivateField(manager, "_funcsByGroups", funcsByGroups);

        // Act
        var result = manager.GetDoodadFuncs(10);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetDoodadFuncs_GroupNotFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcsByGroups = new Dictionary<uint, List<DoodadFunc>>();
        SetPrivateField(manager, "_funcsByGroups", funcsByGroups);

        // Act
        var result = manager.GetDoodadFuncs(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task Exist_MaxUInt32_ReturnsFalse()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.Exist(uint.MaxValue);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GetTemplate_MaxUInt32_ReturnsNull()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(uint.MaxValue);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task MultipleGetTemplateCalls_ReturnConsistentResults()
    {
        // Arrange
        var mockObjId = Mock.Of<IObjectIdManager>();
        var mockDoodadId = Mock.Of<IDoodadIdManager>();
        var mockItem = Mock.Of<IItemManager>();
        var mockHousing = Mock.Of<IHousingManager>();
        var mockSus = Mock.Of<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var template = new DoodadTemplate { Id = 1, GroupId = 100 };
        var templates = new Dictionary<uint, DoodadTemplate> { { 1, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result1 = manager.GetTemplate(1);
        var result2 = manager.GetTemplate(1);
        var result3 = manager.GetTemplate(1);

        // Assert
        await Assert.That(result1).IsNotNull();
        await Assert.That(result2).IsNotNull();
        await Assert.That(result3).IsNotNull();
        await Assert.That(result2.Id).IsEqualTo(result1.Id);
        await Assert.That(result3.Id).IsEqualTo(result2.Id);
    }

    #endregion

    #region Helper Methods

    private static void SetPrivateField(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(obj, value);
    }

    #endregion
}
