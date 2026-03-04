using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

using Moq;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class WorldManagerTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_DoesNotCallDependencies()
    {
        // Arrange
        var mockTickManager = new Mock<ITickManager>();
        var mockWorldIdManager = new Mock<IWorldIdManager>();
        var mockZoneManager = new Mock<IZoneManager>();
        var mockIndunManager = new Mock<IIndunManager>();
        var mockFamilyManager = new Mock<IFamilyManager>();

        // Act
        var manager = new WorldManager(
            mockTickManager.Object,
            mockWorldIdManager.Object,
            new Lazy<IZoneManager>(() => mockZoneManager.Object),
            new Lazy<IIndunManager>(() => mockIndunManager.Object),
            new Lazy<IFamilyManager>(() => mockFamilyManager.Object));

        // Assert
        Assert.NotNull(manager);
        mockTickManager.VerifyNoOtherCalls();
        mockWorldIdManager.VerifyNoOtherCalls();
    }

    #endregion

    #region GetWorld Tests

    [Fact]
    public void GetWorld_ExistingWorld_ReturnsWorldInstance()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate = CreateWorldTemplate(1, "test_world");
        var worldInstance = new WorldInstance(worldTemplate, 0, true, 1);
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        worlds.TryAdd(1, worldInstance);
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorld(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
        Assert.Equal("test_world", result.Template.Name);
    }

    [Fact]
    public void GetWorld_NonExistingWorld_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorld(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetWorld_ZeroId_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorld(0);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetWorld_MultipleWorlds_ReturnsCorrectWorld()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate1 = CreateWorldTemplate(1, "world_1");
        var worldTemplate2 = CreateWorldTemplate(2, "world_2");
        var worldInstance1 = new WorldInstance(worldTemplate1, 0, true, 1);
        var worldInstance2 = new WorldInstance(worldTemplate2, 0, true, 2);
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        worlds.TryAdd(1, worldInstance1);
        worlds.TryAdd(2, worldInstance2);
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result1 = manager.GetWorld(1);
        var result2 = manager.GetWorld(2);

        // Assert
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Equal("world_1", result1.Template.Name);
        Assert.Equal("world_2", result2.Template.Name);
    }

    #endregion

    #region GetWorlds Tests

    [Fact]
    public void GetWorlds_NoWorlds_ReturnsEmptyArray()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorlds();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetWorlds_MultipleWorlds_ReturnsAllWorlds()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate1 = CreateWorldTemplate(1, "world_1");
        var worldTemplate2 = CreateWorldTemplate(2, "world_2");
        var worldInstance1 = new WorldInstance(worldTemplate1, 0, true, 1);
        var worldInstance2 = new WorldInstance(worldTemplate2, 0, true, 2);
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        worlds.TryAdd(1, worldInstance1);
        worlds.TryAdd(2, worldInstance2);
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorlds();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void GetWorlds_SingleWorld_ReturnsArrayWithOneElement()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate = CreateWorldTemplate(1, "test_world");
        var worldInstance = new WorldInstance(worldTemplate, 0, true, 1);
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        worlds.TryAdd(1, worldInstance);
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorlds();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
    }

    #endregion

    #region GetWorldTemplateByName Tests

    [Fact]
    public void GetWorldTemplateByName_ExistingTemplate_ReturnsTemplate()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate = CreateWorldTemplate(1, "main_world");
        var templates = new Dictionary<string, WorldTemplate>
        {
            { "main_world", worldTemplate }
        };
        SetPrivateField(manager, "WorldTemplates", templates);

        // Act
        var result = manager.GetWorldTemplateByName("main_world");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
        Assert.Equal("main_world", result.Name);
    }

    [Fact]
    public void GetWorldTemplateByName_NonExistingTemplate_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var templates = new Dictionary<string, WorldTemplate>();
        SetPrivateField(manager, "WorldTemplates", templates);

        // Act
        var result = manager.GetWorldTemplateByName("non_existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetWorldTemplateByName_NullName_ThrowsArgumentNullException()
    {
        // Arrange
        var manager = CreateWorldManager();
        var templates = new Dictionary<string, WorldTemplate>();
        SetPrivateField(manager, "WorldTemplates", templates);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => manager.GetWorldTemplateByName(null));
    }

    #endregion

    #region GetWorldTemplateByZoneKey Tests

    [Fact]
    public void GetWorldTemplateByZoneKey_ExistingZone_ReturnsTemplate()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate = CreateWorldTemplate(1, "main_world");
        worldTemplate.ZoneKeys.Add(100);
        var worldIdByZoneKey = new Dictionary<uint, uint> { { 100, 1 } };
        var templates = new Dictionary<string, WorldTemplate> { { "main_world", worldTemplate } };
        var worldNames = new List<string> { "", "main_world" };

        SetPrivateField(manager, "_worldIdByZoneKey", worldIdByZoneKey);
        SetPrivateField(manager, "WorldTemplates", templates);
        SetPrivateField(manager, "WorldNames", worldNames);

        // Act
        var result = manager.GetWorldTemplateByZoneKey(100);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("main_world", result.Name);
    }

    [Fact]
    public void GetWorldTemplateByZoneKey_NonExistingZone_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldIdByZoneKey = new Dictionary<uint, uint>();
        SetPrivateField(manager, "_worldIdByZoneKey", worldIdByZoneKey);

        // Act
        var result = manager.GetWorldTemplateByZoneKey(999);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetZoneId Tests

    [Fact]
    public void GetZoneId_ValidPosition_ReturnsZoneId()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate = CreateWorldTemplate(1, "main_world");
        worldTemplate.ZoneKeyByRegions = new uint[16, 16];
        worldTemplate.ZoneKeyByRegions[1, 1] = 100;

        // Act
        // Position (64, 64) should be in region (1, 1) with REGION_SIZE = 64
        var result = manager.GetZoneId(worldTemplate, 64, 64);

        // Assert
        Assert.Equal(100u, result);
    }

    [Fact]
    public void GetZoneId_NullTemplate_ReturnsZero()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetZoneId(null, 100, 100);

        // Assert
        Assert.Equal(0u, result);
    }

    [Fact]
    public void GetZoneId_OutOfBounds_ReturnsZero()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate = CreateWorldTemplate(1, "main_world");
        worldTemplate.ZoneKeyByRegions = new uint[1, 1]; // Very small world

        // Act - Position way out of bounds
        var result = manager.GetZoneId(worldTemplate, 100000, 100000);

        // Assert
        Assert.Equal(0u, result);
    }

    #endregion

    #region GetZoneKeysByWorldId Tests

    [Fact]
    public void GetZoneKeysByWorldId_ExistingWorld_ReturnsZoneKeys()
    {
        // Arrange
        var manager = CreateWorldManager();
        var zoneKeysByWorldId = new Dictionary<uint, List<uint>>
        {
            { 1, new List<uint> { 100, 101, 102 } }
        };
        SetPrivateField(manager, "_zoneKeysByWorldId", zoneKeysByWorldId);

        // Act
        var result = manager.GetZoneKeysByWorldId(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Contains(100u, result);
    }

    [Fact]
    public void GetZoneKeysByWorldId_NonExistingWorld_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateWorldManager();
        var zoneKeysByWorldId = new Dictionary<uint, List<uint>>();
        SetPrivateField(manager, "_zoneKeysByWorldId", zoneKeysByWorldId);

        // Act
        var result = manager.GetZoneKeysByWorldId(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region GetWorldIdByZoneKey Tests

    [Fact]
    public void GetWorldIdByZoneKey_ExistingZone_ReturnsWorldId()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldIdByZoneKey = new Dictionary<uint, uint> { { 100, 1 } };
        SetPrivateField(manager, "_worldIdByZoneKey", worldIdByZoneKey);

        // Act
        var result = manager.GetWorldIdByZoneKey(100);

        // Assert
        Assert.Equal(1u, result);
    }

    [Fact]
    public void GetWorldIdByZoneKey_NonExistingZone_ReturnsMaxValue()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldIdByZoneKey = new Dictionary<uint, uint>();
        SetPrivateField(manager, "_worldIdByZoneKey", worldIdByZoneKey);

        // Act
        var result = manager.GetWorldIdByZoneKey(999);

        // Assert
        Assert.Equal(uint.MaxValue, result);
    }

    #endregion

    #region Character Management Tests

    [Fact]
    public void TryAddCharacter_NewCharacter_ReturnsTrue()
    {
        // Arrange
        var manager = CreateWorldManager();
        var character = new Character(new UnitCustomModelParams())
        {
            ObjId = 100
        };

        // Act
        var result = manager.TryAddCharacter(character);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void TryAddCharacter_DuplicateCharacter_ReturnsFalse()
    {
        // Arrange
        var manager = CreateWorldManager();
        var character = new Character(new UnitCustomModelParams())
        {
            ObjId = 100
        };
        manager.TryAddCharacter(character);

        // Act
        var result = manager.TryAddCharacter(character);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TryRemoveCharacter_ExistingCharacter_ReturnsTrue()
    {
        // Arrange
        var manager = CreateWorldManager();
        var character = new Character(new UnitCustomModelParams())
        {
            ObjId = 100
        };
        manager.TryAddCharacter(character);

        // Act
        var result = manager.TryRemoveCharacter(100);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void TryRemoveCharacter_NonExistingCharacter_ReturnsFalse()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.TryRemoveCharacter(999);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetCharacterByObjId_ExistingCharacter_ReturnsCharacter()
    {
        // Arrange
        var manager = CreateWorldManager();
        var character = new Character(new UnitCustomModelParams())
        {
            ObjId = 100,
            Name = "TestPlayer"
        };
        manager.TryAddCharacter(character);

        // Act
        var result = manager.GetCharacterByObjId(100);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestPlayer", result.Name);
    }

    [Fact]
    public void GetCharacterByObjId_NonExistingCharacter_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetCharacterByObjId(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCharacter_ExistingCharacterByName_ReturnsCharacter()
    {
        // Arrange
        var manager = CreateWorldManager();
        var character = new Character(new UnitCustomModelParams())
        {
            ObjId = 100,
            Name = "TestPlayer"
        };
        manager.TryAddCharacter(character);

        // Act
        var result = manager.GetCharacter("TestPlayer");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestPlayer", result.Name);
    }

    [Fact]
    public void GetCharacter_ExistingCharacterByNameCaseInsensitive_ReturnsCharacter()
    {
        // Arrange
        var manager = CreateWorldManager();
        var character = new Character(new UnitCustomModelParams())
        {
            ObjId = 100,
            Name = "TestPlayer"
        };
        manager.TryAddCharacter(character);

        // Act
        var result = manager.GetCharacter("testplayer");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestPlayer", result.Name);
    }

    [Fact]
    public void GetCharacter_NonExistingCharacter_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetCharacter("NonExistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetAllCharacters_WithCharacters_ReturnsAllCharacters()
    {
        // Arrange
        var manager = CreateWorldManager();
        var character1 = new Character(new UnitCustomModelParams()) { ObjId = 1, Name = "Player1" };
        var character2 = new Character(new UnitCustomModelParams()) { ObjId = 2, Name = "Player2" };
        manager.TryAddCharacter(character1);
        manager.TryAddCharacter(character2);

        // Act
        var result = manager.GetAllCharacters();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetAllCharacters_NoCharacters_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetAllCharacters();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region World Interaction Group Tests

    [Fact]
    public void GetWorldInteractionGroup_ExistingGroup_ReturnsGroup()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldInteractionGroups = new Dictionary<uint, WorldInteractionGroup>
        {
            { 1, WorldInteractionGroup.Craft }
        };
        SetPrivateField(manager, "_worldInteractionGroups", worldInteractionGroups);

        // Act
        var result = manager.GetWorldInteractionGroup(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(WorldInteractionGroup.Craft, result);
    }

    [Fact]
    public void GetWorldInteractionGroup_NonExistingGroup_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldInteractionGroups = new Dictionary<uint, WorldInteractionGroup>();
        SetPrivateField(manager, "_worldInteractionGroups", worldInteractionGroups);

        // Act
        var result = manager.GetWorldInteractionGroup(999);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetWorldsByTemplate Tests

    [Fact]
    public void GetWorldsByTemplate_ExistingWorlds_ReturnsMatchingWorlds()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate1 = CreateWorldTemplate(1, "template_1");
        var worldTemplate2 = CreateWorldTemplate(2, "template_2");
        var worldInstance1 = new WorldInstance(worldTemplate1, 0, true, 1);
        var worldInstance2 = new WorldInstance(worldTemplate2, 0, true, 2);
        var worldInstance3 = new WorldInstance(worldTemplate1, 0, true, 3);

        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        worlds.TryAdd(1, worldInstance1);
        worlds.TryAdd(2, worldInstance2);
        worlds.TryAdd(3, worldInstance3);
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorldsByTemplate(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetWorldsByTemplate_NoMatchingWorlds_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorldsByTemplate(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region RemoveWorld Tests

    [Fact]
    public void RemoveWorld_ExistingWorld_RemovesWorld()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate = CreateWorldTemplate(1, "test_world");
        var worldInstance = new WorldInstance(worldTemplate, 0, true, 1);
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        worlds.TryAdd(1, worldInstance);
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        manager.RemoveWorld(1);

        // Assert
        Assert.Null(manager.GetWorld(1));
    }

    [Fact]
    public void RemoveWorld_NonExistingWorld_DoesNotThrow()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act & Assert
        manager.RemoveWorld(999); // Should not throw
    }

    #endregion

    #region GetCharacterById Tests

    [Fact]
    public void GetCharacterById_ExistingCharacter_ReturnsCharacter()
    {
        // Arrange
        var manager = CreateWorldManager();
        var character = new Character(new UnitCustomModelParams())
        {
            ObjId = 100,
            Id = 50,
            Name = "TestPlayer"
        };
        manager.TryAddCharacter(character);

        // Act
        var result = manager.GetCharacterById(50);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TestPlayer", result.Name);
    }

    [Fact]
    public void GetCharacterById_NonExistingCharacter_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetCharacterById(999);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetAllNpcsFromWorld Tests

    [Fact]
    public void GetAllNpcsFromWorld_ExistingWorld_ReturnsNpcs()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate = CreateWorldTemplate(1, "test_world");
        var worldInstance = new WorldInstance(worldTemplate, 0, true, 1);

        // Use reflection to set regions array
        var regionsField = typeof(WorldInstance).GetField("Regions", BindingFlags.Public | BindingFlags.Instance);
        regionsField?.SetValue(worldInstance, new Region[1, 1]);

        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        worlds.TryAdd(1, worldInstance);
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetAllNpcsFromWorld(1);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result); // Empty because no NPCs were added
    }

    [Fact]
    public void GetAllNpcsFromWorld_NonExistingWorld_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetAllNpcsFromWorld(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region GetAllDoodadsFromWorld Tests

    [Fact]
    public void GetAllDoodadsFromWorld_ExistingWorld_ReturnsDoodads()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate = CreateWorldTemplate(1, "test_world");
        var worldInstance = new WorldInstance(worldTemplate, 0, true, 1);
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        worlds.TryAdd(1, worldInstance);
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetAllDoodadsFromWorld(1);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result); // Empty because no doodads were added
    }

    [Fact]
    public void GetAllDoodadsFromWorld_NonExistingWorld_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetAllDoodadsFromWorld(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region GetAllSlavesFromWorld Tests

    [Fact]
    public void GetAllSlavesFromWorld_ExistingWorld_ReturnsSlaves()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate = CreateWorldTemplate(1, "test_world");
        var worldInstance = new WorldInstance(worldTemplate, 0, true, 1);
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        worlds.TryAdd(1, worldInstance);
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetAllSlavesFromWorld(1);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result); // Empty because no slaves were added
    }

    [Fact]
    public void GetAllSlavesFromWorld_NonExistingWorld_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetAllSlavesFromWorld(999);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    #endregion

    #region IsSnowing Property Tests

    [Fact]
    public void IsSnowing_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        manager.IsSnowing = true;

        // Assert
        Assert.True(manager.IsSnowing);
    }

    [Fact]
    public void IsSnowing_DefaultValue_ReturnsFalse()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act & Assert
        Assert.False(manager.IsSnowing);
    }

    #endregion

    #region AreaShape Tests

    [Fact]
    public void GetAreaShapeById_ExistingShape_ReturnsShape()
    {
        // Arrange
        var manager = CreateWorldManager();
        var areaShape = new AreaShape { Id = 1, Type = AreaShapeType.Sphere, Value1 = 100 };
        var areaShapes = new ConcurrentDictionary<uint, AreaShape>();
        areaShapes.TryAdd(1, areaShape);
        SetPrivateField(manager, "_areaShapes", areaShapes);

        // Act
        var result = manager.GetAreaShapeById(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
        Assert.Equal(AreaShapeType.Sphere, result.Type);
    }

    [Fact]
    public void GetAreaShapeById_NonExistingShape_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var areaShapes = new ConcurrentDictionary<uint, AreaShape>();
        SetPrivateField(manager, "_areaShapes", areaShapes);

        // Act
        var result = manager.GetAreaShapeById(999);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region TargetOrSelf Tests

    [Fact]
    public void GetTargetOrSelf_WithTargetName_ReturnsTarget()
    {
        // Arrange
        var manager = CreateWorldManager();
        var character = new Character(new UnitCustomModelParams())
        {
            ObjId = 100,
            Name = "SourcePlayer"
        };
        var target = new Character(new UnitCustomModelParams())
        {
            ObjId = 200,
            Name = "TargetPlayer"
        };
        manager.TryAddCharacter(target);

        // Act
        var result = manager.GetTargetOrSelf(character, "TargetPlayer", out var firstNonNameArgument);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("TargetPlayer", result.Name);
        Assert.Equal(1, firstNonNameArgument);
    }

    [Fact]
    public void GetTargetOrSelf_WithNonExistingTargetName_ReturnsSelf()
    {
        // Arrange
        var manager = CreateWorldManager();
        var character = new Character(new UnitCustomModelParams())
        {
            ObjId = 100,
            Name = "SourcePlayer"
        };

        // Act
        var result = manager.GetTargetOrSelf(character, "NonExisting", out var firstNonNameArgument);

        // Assert
        Assert.Equal("SourcePlayer", result.Name);
        Assert.Equal(0, firstNonNameArgument);
    }

    [Fact]
    public void GetTargetOrSelf_WithNullTargetName_ReturnsSelf()
    {
        // Arrange
        var manager = CreateWorldManager();
        var character = new Character(new UnitCustomModelParams())
        {
            ObjId = 100,
            Name = "SourcePlayer"
        };

        // Act
        var result = manager.GetTargetOrSelf(character, null, out var firstNonNameArgument);

        // Assert
        Assert.Equal("SourcePlayer", result.Name);
        Assert.Equal(0, firstNonNameArgument);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public void GetWorld_MaxUintId_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorld(uint.MaxValue);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCharacter_NullName_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetCharacter(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCharacter_EmptyName_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetCharacter("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetZoneId_NegativeCoordinates_ReturnsZero()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate = CreateWorldTemplate(1, "main_world");
        worldTemplate.ZoneKeyByRegions = new uint[16, 16];

        // Act
        var result = manager.GetZoneId(worldTemplate, -100, -100);

        // Assert
        Assert.Equal(0u, result);
    }

    [Fact]
    public void TryAddCharacter_NullCharacter_ThrowsNullReferenceException()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act & Assert
        Assert.Throws<NullReferenceException>(() => manager.TryAddCharacter(null));
    }

    [Fact]
    public void TryRemoveCharacter_ZeroId_ReturnsFalse()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.TryRemoveCharacter(0);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Multiple Operations Tests

    [Fact]
    public void MultipleOperations_AddAndRemoveCharacter_CharacterRemoved()
    {
        // Arrange
        var manager = CreateWorldManager();
        var character = new Character(new UnitCustomModelParams())
        {
            ObjId = 100,
            Name = "TestPlayer"
        };

        // Act - Add character
        var addResult = manager.TryAddCharacter(character);
        Assert.True(addResult);
        Assert.NotNull(manager.GetCharacterByObjId(100));

        // Act - Remove character
        var removeResult = manager.TryRemoveCharacter(100);
        Assert.True(removeResult);
        Assert.Null(manager.GetCharacterByObjId(100));
    }

    [Fact]
    public void MultipleOperations_AddMultipleWorlds_AllWorldsAccessible()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();

        // Add multiple worlds
        for (uint i = 1; i <= 5; i++)
        {
            var worldTemplate = CreateWorldTemplate(i, $"world_{i}");
            var worldInstance = new WorldInstance(worldTemplate, 0, true, i);
            worlds.TryAdd(i, worldInstance);
        }
        SetPrivateField(manager, "_worlds", worlds);

        // Act & Assert
        Assert.Equal(5, manager.GetWorlds().Length);
        for (uint i = 1; i <= 5; i++)
        {
            Assert.NotNull(manager.GetWorld(i));
        }
    }

    [Fact]
    public void MultipleOperations_RemoveAllWorlds_WorldsEmpty()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        var worldTemplate = CreateWorldTemplate(1, "test_world");
        var worldInstance = new WorldInstance(worldTemplate, 0, true, 1);
        worlds.TryAdd(1, worldInstance);
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        manager.RemoveWorld(1);

        // Assert
        Assert.Empty(manager.GetWorlds());
    }

    #endregion

    #region Constants Tests

    [Fact]
    public void Constants_CellSize_ReturnsCorrectValue()
    {
        // Assert
        Assert.Equal(1024, WorldManager.CELL_SIZE);
    }

    [Fact]
    public void Constants_RegionSize_ReturnsCorrectValue()
    {
        // Assert
        Assert.Equal(64, WorldManager.REGION_SIZE);
    }

    [Fact]
    public void Constants_SectorsPerCell_ReturnsCorrectValue()
    {
        // Assert
        Assert.Equal(16, WorldManager.SECTORS_PER_CELL); // 1024 / 64 = 16
    }

    [Fact]
    public void Constants_DefaultCombatTimeout_ReturnsCorrectValue()
    {
        // Assert
        Assert.Equal(15f, WorldManager.DefaultCombatTimeout);
    }

    #endregion

    #region Helper Methods

    private static WorldManager CreateWorldManager()
    {
        var mockTickManager = new Mock<ITickManager>();
        var mockWorldIdManager = new Mock<IWorldIdManager>();
        var mockZoneManager = new Mock<IZoneManager>();
        var mockIndunManager = new Mock<IIndunManager>();
        var mockFamilyManager = new Mock<IFamilyManager>();

        return new WorldManager(
            mockTickManager.Object,
            mockWorldIdManager.Object,
            new Lazy<IZoneManager>(() => mockZoneManager.Object),
            new Lazy<IIndunManager>(() => mockIndunManager.Object),
            new Lazy<IFamilyManager>(() => mockFamilyManager.Object));
    }

    private static WorldTemplate CreateWorldTemplate(uint id, string name)
    {
        var template = new WorldTemplate
        {
            Id = id,
            Name = name,
            ZoneKeys = new List<uint>(),
            CellX = 2,
            CellY = 2,
            ZoneKeyByRegions = new uint[32, 32]
        };
        return template;
    }

    private static void SetPrivateField(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        if (field == null)
        {
            // Try to find property
            var property = obj.GetType().GetProperty(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            property?.SetValue(obj, value);
        }
        else
        {
            field.SetValue(obj, value);
        }
    }

    #endregion
}
