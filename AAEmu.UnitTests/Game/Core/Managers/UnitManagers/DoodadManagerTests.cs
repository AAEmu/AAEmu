using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Details;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.World;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers.UnitManagers;

public class DoodadManagerTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_DoesNotCallDeps()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();

        // Act
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        // Assert
        Assert.NotNull(manager);
        mockObjId.VerifyNoOtherCalls();
        mockDoodadId.VerifyNoOtherCalls();
        mockItem.VerifyNoOtherCalls();
        mockHousing.VerifyNoOtherCalls();
        mockSus.VerifyNoOtherCalls();
    }

    #endregion

    #region Exist Tests

    [Fact]
    public void Exist_TemplateExists_ReturnsTrue()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var template = new DoodadTemplate { Id = 1 };
        var templates = new Dictionary<uint, DoodadTemplate> { { 1, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.Exist(1);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Exist_TemplateDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.Exist(999);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Exist_ZeroId_ReturnsFalse()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.Exist(0);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetTemplate Tests

    [Fact]
    public void GetTemplate_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var template = new DoodadTemplate { Id = 1, GroupId = 100 };
        var templates = new Dictionary<uint, DoodadTemplate> { { 1, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
        Assert.Equal(100u, result.GroupId);
    }

    [Fact]
    public void GetTemplate_TemplateDoesNotExist_ReturnsNull()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
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
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(0);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Create Tests

    // Note: Create tests that involve WorldInstance require WorldManager singleton initialization,
    // which makes them integration tests rather than unit tests. These tests are omitted here
    // but would be covered in integration testing.

    [Fact]
    public void Create_WithInvalidTemplate_ReturnsNull()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        var mockWorld = new Mock<WorldInstance>(MockBehavior.Loose, new WorldTemplate { Id = 1, Name = "TestWorld" }, 1u, false, 1u);

        // Act
        var result = manager.Create(mockWorld.Object, 0, 999, null, true);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Create_WithNullWorld_ReturnsNull()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var template = new DoodadTemplate { Id = 1 };
        template.FuncGroups.Add(new DoodadFuncGroups { Id = 10, Almighty = 1, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Start });
        var templates = new Dictionary<uint, DoodadTemplate> { { 1, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.Create(null, 0, 1, null, true);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetFunc Tests

    [Fact]
    public void GetFunc_ByFuncId_ReturnsFunc()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var func = new DoodadFunc { FuncKey = 1, GroupId = 10, FuncId = 100 };
        var funcsById = new Dictionary<uint, DoodadFunc> { { 1, func } };
        SetPrivateField(manager, "_funcsById", funcsById);

        // Act
        var result = manager.GetFunc(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1u, result.FuncKey);
        Assert.Equal(10u, result.GroupId);
    }

    [Fact]
    public void GetFunc_ByFuncId_NotFound_ReturnsNull()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcsById = new Dictionary<uint, DoodadFunc>();
        SetPrivateField(manager, "_funcsById", funcsById);

        // Act
        var result = manager.GetFunc(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetFunc_ByGroupIdAndSkillId_ReturnsFunc()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
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
        Assert.NotNull(result);
        Assert.Equal(50u, result.SkillId);
    }

    [Fact]
    public void GetFunc_ByGroupId_GroupNotFound_ReturnsNull()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcsByGroups = new Dictionary<uint, List<DoodadFunc>>();
        SetPrivateField(manager, "_funcsByGroups", funcsByGroups);

        // Act
        var result = manager.GetFunc(999, 0);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetFuncsForGroup Tests

    [Fact]
    public void GetFuncsForGroup_GroupExists_ReturnsFuncs()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
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
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetFuncsForGroup_GroupNotFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcsByGroups = new Dictionary<uint, List<DoodadFunc>>();
        SetPrivateField(manager, "_funcsByGroups", funcsByGroups);

        // Act
        var result = manager.GetFuncsForGroup(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region GetPhaseFunc Tests

    [Fact]
    public void GetPhaseFunc_GroupExists_ReturnsPhaseFuncs()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
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
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetPhaseFunc_GroupNotFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var phaseFuncsByGroups = new Dictionary<uint, List<DoodadPhaseFunc>>();
        SetPrivateField(manager, "_phaseFuncs", phaseFuncsByGroups);

        // Act
        var result = manager.GetPhaseFunc(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region GetDoodadFuncGroups Tests

    [Fact]
    public void GetDoodadFuncGroups_TemplateExists_ReturnsGroups()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var template = new DoodadTemplate { Id = 1 };
        template.FuncGroups.Add(new DoodadFuncGroups { Id = 10, Almighty = 1, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Start });
        template.FuncGroups.Add(new DoodadFuncGroups { Id = 20, Almighty = 1, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal });
        var templates = new Dictionary<uint, DoodadTemplate> { { 1, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetDoodadFuncGroups(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetDoodadFuncGroups_TemplateNotFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetDoodadFuncGroups(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region GetDoodadFuncGroupsId Tests

    [Fact]
    public void GetDoodadFuncGroupsId_TemplateExists_ReturnsGroupIds()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var template = new DoodadTemplate { Id = 1 };
        template.FuncGroups.Add(new DoodadFuncGroups { Id = 10, Almighty = 1, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Start });
        template.FuncGroups.Add(new DoodadFuncGroups { Id = 20, Almighty = 1, GroupKindId = DoodadFuncGroups.DoodadFuncGroupKind.Normal });
        var templates = new Dictionary<uint, DoodadTemplate> { { 1, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetDoodadFuncGroupsId(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains(10u, result);
        Assert.Contains(20u, result);
    }

    [Fact]
    public void GetDoodadFuncGroupsId_TemplateNotFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetDoodadFuncGroupsId(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region GetDoodadFuncConsumeChangerItemList Tests

    [Fact]
    public void GetDoodadFuncConsumeChangerItemList_ItemsExist_ReturnsItemIds()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
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
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains(100u, result);
        Assert.Contains(200u, result);
    }

    [Fact]
    public void GetDoodadFuncConsumeChangerItemList_NoItemsFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var consumeItems = new Dictionary<uint, DoodadFuncConsumeChangerItem>();
        SetPrivateField(manager, "_doodadFuncConsumeChangerItem", consumeItems);

        // Act
        var result = manager.GetDoodadFuncConsumeChangerItemList(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region GetTreasureChestTemplateIds Tests

    [Fact]
    public void GetTreasureChestTemplateIds_ReturnsChestTemplates()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
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
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Contains(1u, result);
        Assert.Contains(2u, result);
        Assert.Contains(4u, result);
        Assert.DoesNotContain(3u, result);
        Assert.DoesNotContain(5u, result);
    }

    [Fact]
    public void GetTreasureChestTemplateIds_NoTemplates_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTreasureChestTemplateIds();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region GetDoodadPhaseFuncs Tests

    [Fact]
    public void GetDoodadPhaseFuncs_GroupExists_ReturnsPhaseFuncs()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
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
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetDoodadPhaseFuncs_GroupNotFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var phaseFuncsDict = new Dictionary<uint, List<DoodadPhaseFunc>>();
        SetPrivateField(manager, "_phaseFuncs", phaseFuncsDict);

        // Act
        var result = manager.GetDoodadPhaseFuncs(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region GetFuncTemplate Tests

    [Fact]
    public void GetFuncTemplate_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
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
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
    }

    [Fact]
    public void GetFuncTemplate_TypeNotFound_ReturnsNull()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcTemplates = new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>();
        SetPrivateField(manager, "_funcTemplates", funcTemplates);

        // Act
        var result = manager.GetFuncTemplate(1, "NonExistentType");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetFuncTemplate_TemplateNotFound_ReturnsNull()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcTemplates = new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>
        {
            { "DoodadFuncUse", new Dictionary<uint, DoodadFuncTemplate>() }
        };
        SetPrivateField(manager, "_funcTemplates", funcTemplates);

        // Act
        var result = manager.GetFuncTemplate(999, "DoodadFuncUse");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetPhaseFuncTemplate Tests

    [Fact]
    public void GetPhaseFuncTemplate_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
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
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
    }

    [Fact]
    public void GetPhaseFuncTemplate_TypeNotFound_ReturnsNull()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var phaseFuncTemplates = new Dictionary<string, Dictionary<uint, DoodadPhaseFuncTemplate>>();
        SetPrivateField(manager, "_phaseFuncTemplates", phaseFuncTemplates);

        // Act
        var result = manager.GetPhaseFuncTemplate(1, "NonExistentType");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetDoodadFuncs Tests

    [Fact]
    public void GetDoodadFuncs_GroupExists_ReturnsFuncs()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
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
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetDoodadFuncs_GroupNotFound_ReturnsEmptyList()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var funcsByGroups = new Dictionary<uint, List<DoodadFunc>>();
        SetPrivateField(manager, "_funcsByGroups", funcsByGroups);

        // Act
        var result = manager.GetDoodadFuncs(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Exist_MaxUInt32_ReturnsFalse()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.Exist(uint.MaxValue);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetTemplate_MaxUInt32_ReturnsNull()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var templates = new Dictionary<uint, DoodadTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(uint.MaxValue);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void MultipleGetTemplateCalls_ReturnConsistentResults()
    {
        // Arrange
        var mockObjId = new Mock<IObjectIdManager>();
        var mockDoodadId = new Mock<IDoodadIdManager>();
        var mockItem = new Mock<IItemManager>();
        var mockHousing = new Mock<IHousingManager>();
        var mockSus = new Mock<ISusManager>();
        var manager = new DoodadManager(mockObjId.Object, mockDoodadId.Object, mockItem.Object, new Lazy<IHousingManager>(() => mockHousing.Object), mockSus.Object);

        var template = new DoodadTemplate { Id = 1, GroupId = 100 };
        var templates = new Dictionary<uint, DoodadTemplate> { { 1, template } };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result1 = manager.GetTemplate(1);
        var result2 = manager.GetTemplate(1);
        var result3 = manager.GetTemplate(1);

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.NotNull(result3);
        Assert.Equal(result1.Id, result2.Id);
        Assert.Equal(result2.Id, result3.Id);
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
