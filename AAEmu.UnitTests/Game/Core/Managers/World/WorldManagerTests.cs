using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class WorldManagerTests
{
    #region Constructor Tests

    [Test]
    public async Task Constructor_DoesNotCallDependencies()
    {
        // Arrange
        var mockTickManager = Mock.Of<ITickManager>();
        var mockWorldIdManager = Mock.Of<IWorldIdManager>();
        var mockZoneManager = Mock.Of<IZoneManager>();
        var mockIndunManager = Mock.Of<IIndunManager>();
        var mockFamilyManager = Mock.Of<IFamilyManager>();

        // Act
        var manager = new WorldManager(
            mockTickManager.Object,
            mockWorldIdManager.Object,
            new Lazy<IZoneManager>(() => mockZoneManager.Object),
            new Lazy<IIndunManager>(() => mockIndunManager.Object),
            new Lazy<IFamilyManager>(() => mockFamilyManager.Object));

        // Assert
        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockTickManager);
        Mock.VerifyNoOtherCalls(mockWorldIdManager);
    }

    #endregion

    #region GetWorld Tests

    [Test]
    public async Task GetWorld_ExistingWorld_ReturnsWorldInstance()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
        await Assert.That(result.Template.Name).IsEqualTo("test_world");
    }

    [Test]
    public async Task GetWorld_NonExistingWorld_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorld(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetWorld_ZeroId_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorld(0);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetWorld_MultipleWorlds_ReturnsCorrectWorld()
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
        await Assert.That(result1).IsNotNull();
        await Assert.That(result2).IsNotNull();
        await Assert.That(result1.Template.Name).IsEqualTo("world_1");
        await Assert.That(result2.Template.Name).IsEqualTo("world_2");
    }

    #endregion

    #region GetWorlds Tests

    [Test]
    public async Task GetWorlds_NoWorlds_ReturnsEmptyArray()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorlds();

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetWorlds_MultipleWorlds_ReturnsAllWorlds()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Length).IsEqualTo(2);
    }

    [Test]
    public async Task GetWorlds_SingleWorld_ReturnsArrayWithOneElement()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result).HasSingleItem();
    }

    #endregion

    #region GetWorldTemplateByName Tests

    [Test]
    public async Task GetWorldTemplateByName_ExistingTemplate_ReturnsTemplate()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
        await Assert.That(result.Name).IsEqualTo("main_world");
    }

    [Test]
    public async Task GetWorldTemplateByName_NonExistingTemplate_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var templates = new Dictionary<string, WorldTemplate>();
        SetPrivateField(manager, "WorldTemplates", templates);

        // Act
        var result = manager.GetWorldTemplateByName("non_existent");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
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

    [Test]
    public async Task GetWorldTemplateByZoneKey_ExistingZone_ReturnsTemplate()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Name).IsEqualTo("main_world");
    }

    [Test]
    public async Task GetWorldTemplateByZoneKey_NonExistingZone_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldIdByZoneKey = new Dictionary<uint, uint>();
        SetPrivateField(manager, "_worldIdByZoneKey", worldIdByZoneKey);

        // Act
        var result = manager.GetWorldTemplateByZoneKey(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetZoneId Tests

    [Test]
    public async Task GetZoneId_ValidPosition_ReturnsZoneId()
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
        await Assert.That(result).IsEqualTo(100u);
    }

    [Test]
    public async Task GetZoneId_NullTemplate_ReturnsZero()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetZoneId(null, 100, 100);

        // Assert
        await Assert.That(result).IsEqualTo(0u);
    }

    [Test]
    public async Task GetZoneId_OutOfBounds_ReturnsZero()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate = CreateWorldTemplate(1, "main_world");
        worldTemplate.ZoneKeyByRegions = new uint[1, 1]; // Very small world

        // Act - Position way out of bounds
        var result = manager.GetZoneId(worldTemplate, 100000, 100000);

        // Assert
        await Assert.That(result).IsEqualTo(0u);
    }

    #endregion

    #region GetZoneKeysByWorldId Tests

    [Test]
    public async Task GetZoneKeysByWorldId_ExistingWorld_ReturnsZoneKeys()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result).Contains(100u);
    }

    [Test]
    public async Task GetZoneKeysByWorldId_NonExistingWorld_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateWorldManager();
        var zoneKeysByWorldId = new Dictionary<uint, List<uint>>();
        SetPrivateField(manager, "_zoneKeysByWorldId", zoneKeysByWorldId);

        // Act
        var result = manager.GetZoneKeysByWorldId(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetWorldIdByZoneKey Tests

    [Test]
    public async Task GetWorldIdByZoneKey_ExistingZone_ReturnsWorldId()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldIdByZoneKey = new Dictionary<uint, uint> { { 100, 1 } };
        SetPrivateField(manager, "_worldIdByZoneKey", worldIdByZoneKey);

        // Act
        var result = manager.GetWorldIdByZoneKey(100);

        // Assert
        await Assert.That(result).IsEqualTo(1u);
    }

    [Test]
    public async Task GetWorldIdByZoneKey_NonExistingZone_ReturnsMaxValue()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldIdByZoneKey = new Dictionary<uint, uint>();
        SetPrivateField(manager, "_worldIdByZoneKey", worldIdByZoneKey);

        // Act
        var result = manager.GetWorldIdByZoneKey(999);

        // Assert
        await Assert.That(result).IsEqualTo(uint.MaxValue);
    }

    #endregion

    #region Character Management Tests

    [Test]
    public async Task TryAddCharacter_NewCharacter_ReturnsTrue()
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
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryAddCharacter_DuplicateCharacter_ReturnsFalse()
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
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task TryRemoveCharacter_ExistingCharacter_ReturnsTrue()
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
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task TryRemoveCharacter_NonExistingCharacter_ReturnsFalse()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.TryRemoveCharacter(999);

        // Assert
        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task GetCharacterByObjId_ExistingCharacter_ReturnsCharacter()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Name).IsEqualTo("TestPlayer");
    }

    [Test]
    public async Task GetCharacterByObjId_NonExistingCharacter_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetCharacterByObjId(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetCharacter_ExistingCharacterByName_ReturnsCharacter()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Name).IsEqualTo("TestPlayer");
    }

    [Test]
    public async Task GetCharacter_ExistingCharacterByNameCaseInsensitive_ReturnsCharacter()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Name).IsEqualTo("TestPlayer");
    }

    [Test]
    public async Task GetCharacter_NonExistingCharacter_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetCharacter("NonExistent");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetAllCharacters_WithCharacters_ReturnsAllCharacters()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetAllCharacters_NoCharacters_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetAllCharacters();

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region World Interaction Group Tests

    [Test]
    public async Task GetWorldInteractionGroup_ExistingGroup_ReturnsGroup()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEqualTo(WorldInteractionGroup.Craft);
    }

    [Test]
    public async Task GetWorldInteractionGroup_NonExistingGroup_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldInteractionGroups = new Dictionary<uint, WorldInteractionGroup>();
        SetPrivateField(manager, "_worldInteractionGroups", worldInteractionGroups);

        // Act
        var result = manager.GetWorldInteractionGroup(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetWorldsByTemplate Tests

    [Test]
    public async Task GetWorldsByTemplate_ExistingWorlds_ReturnsMatchingWorlds()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetWorldsByTemplate_NoMatchingWorlds_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorldsByTemplate(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region RemoveWorld Tests

    [Test]
    public async Task RemoveWorld_ExistingWorld_RemovesWorld()
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
        await Assert.That(manager.GetWorld(1)).IsNull();
    }

    [Test]
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

    [Test]
    public async Task GetCharacterById_ExistingCharacter_ReturnsCharacter()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Name).IsEqualTo("TestPlayer");
    }

    [Test]
    public async Task GetCharacterById_NonExistingCharacter_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetCharacterById(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetAllNpcsFromWorld Tests

    [Test]
    public async Task GetAllNpcsFromWorld_ExistingWorld_ReturnsNpcs()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty(); // Empty because no NPCs were added
    }

    [Test]
    public async Task GetAllNpcsFromWorld_NonExistingWorld_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetAllNpcsFromWorld(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetAllDoodadsFromWorld Tests

    [Test]
    public async Task GetAllDoodadsFromWorld_ExistingWorld_ReturnsDoodads()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty(); // Empty because no doodads were added
    }

    [Test]
    public async Task GetAllDoodadsFromWorld_NonExistingWorld_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetAllDoodadsFromWorld(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region GetAllSlavesFromWorld Tests

    [Test]
    public async Task GetAllSlavesFromWorld_ExistingWorld_ReturnsSlaves()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty(); // Empty because no slaves were added
    }

    [Test]
    public async Task GetAllSlavesFromWorld_NonExistingWorld_ReturnsEmptyList()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetAllSlavesFromWorld(999);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region IsSnowing Property Tests

    [Test]
    public async Task IsSnowing_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        manager.IsSnowing = true;

        // Assert
        await Assert.That(manager.IsSnowing).IsTrue();
    }

    [Test]
    public async Task IsSnowing_DefaultValue_ReturnsFalse()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act & Assert
        await Assert.That(manager.IsSnowing).IsFalse();
    }

    #endregion

    #region AreaShape Tests

    [Test]
    public async Task GetAreaShapeById_ExistingShape_ReturnsShape()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
        await Assert.That(result.Type).IsEqualTo(AreaShapeType.Sphere);
    }

    [Test]
    public async Task GetAreaShapeById_NonExistingShape_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var areaShapes = new ConcurrentDictionary<uint, AreaShape>();
        SetPrivateField(manager, "_areaShapes", areaShapes);

        // Act
        var result = manager.GetAreaShapeById(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region TargetOrSelf Tests

    [Test]
    public async Task GetTargetOrSelf_WithTargetName_ReturnsTarget()
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
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Name).IsEqualTo("TargetPlayer");
        await Assert.That(firstNonNameArgument).IsEqualTo(1);
    }

    [Test]
    public async Task GetTargetOrSelf_WithNonExistingTargetName_ReturnsSelf()
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
        await Assert.That(result.Name).IsEqualTo("SourcePlayer");
        await Assert.That(firstNonNameArgument).IsEqualTo(0);
    }

    [Test]
    public async Task GetTargetOrSelf_WithNullTargetName_ReturnsSelf()
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
        await Assert.That(result.Name).IsEqualTo("SourcePlayer");
        await Assert.That(firstNonNameArgument).IsEqualTo(0);
    }

    #endregion

    #region Edge Cases Tests

    [Test]
    public async Task GetWorld_MaxUintId_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worlds = new ConcurrentDictionary<uint, WorldInstance>();
        SetPrivateField(manager, "_worlds", worlds);

        // Act
        var result = manager.GetWorld(uint.MaxValue);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetCharacter_NullName_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetCharacter(null);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetCharacter_EmptyName_ReturnsNull()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.GetCharacter("");

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetZoneId_NegativeCoordinates_ReturnsZero()
    {
        // Arrange
        var manager = CreateWorldManager();
        var worldTemplate = CreateWorldTemplate(1, "main_world");
        worldTemplate.ZoneKeyByRegions = new uint[16, 16];

        // Act
        var result = manager.GetZoneId(worldTemplate, -100, -100);

        // Assert
        await Assert.That(result).IsEqualTo(0u);
    }

    [Test]
    public void TryAddCharacter_NullCharacter_ThrowsNullReferenceException()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act & Assert
        Assert.Throws<NullReferenceException>(() => manager.TryAddCharacter(null));
    }

    [Test]
    public async Task TryRemoveCharacter_ZeroId_ReturnsFalse()
    {
        // Arrange
        var manager = CreateWorldManager();

        // Act
        var result = manager.TryRemoveCharacter(0);

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    #region Multiple Operations Tests

    [Test]
    public async Task MultipleOperations_AddAndRemoveCharacter_CharacterRemoved()
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
        await Assert.That(addResult).IsTrue();
        await Assert.That(manager.GetCharacterByObjId(100)).IsNotNull();

        // Act - Remove character
        var removeResult = manager.TryRemoveCharacter(100);
        await Assert.That(removeResult).IsTrue();
        await Assert.That(manager.GetCharacterByObjId(100)).IsNull();
    }

    [Test]
    public async Task MultipleOperations_AddMultipleWorlds_AllWorldsAccessible()
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
        await Assert.That(manager.GetWorlds().Length).IsEqualTo(5);
        for (uint i = 1; i <= 5; i++)
        {
            await Assert.That(manager.GetWorld(i)).IsNotNull();
        }
    }

    [Test]
    public async Task MultipleOperations_RemoveAllWorlds_WorldsEmpty()
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
        await Assert.That(manager.GetWorlds()).IsEmpty();
    }

    #endregion

    #region Constants Tests

    [Test]
    public async Task Constants_CellSize_ReturnsCorrectValue()
    {
        // Assert
        await Assert.That(WorldManager.CELL_SIZE).IsEqualTo(1024);
    }

    [Test]
    public async Task Constants_RegionSize_ReturnsCorrectValue()
    {
        // Assert
        await Assert.That(WorldManager.REGION_SIZE).IsEqualTo(64);
    }

    [Test]
    public async Task Constants_SectorsPerCell_ReturnsCorrectValue()
    {
        // Assert
        await Assert.That(WorldManager.SECTORS_PER_CELL).IsEqualTo(16); // 1024 / 64 = 16
    }

    [Test]
    public async Task Constants_DefaultCombatTimeout_ReturnsCorrectValue()
    {
        // Assert
        await Assert.That(WorldManager.DefaultCombatTimeout).IsEqualTo(15f);
    }

    #endregion

    #region Initialize Tests

    [Test]
    public async Task Initialize_SubscribesActiveRegionTickAsAsync()
    {
        var handler = new TickManager.TickEventHandler();
        var mockTickManager = Mock.Of<ITickManager>();
        mockTickManager.OnTick.Returns(handler);

        var manager = new WorldManager(
            mockTickManager.Object,
            Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));

        manager.Initialize();

        var queueField = typeof(TickManager.TickEventHandler)
            .GetField("_eventsToAdd", BindingFlags.NonPublic | BindingFlags.Instance);
        var pending = (Queue<TickManager.TickEventEntity>)queueField!.GetValue(handler)!;

        var activeRegion = pending.FirstOrDefault(e =>
            e.Event.Method.Name.Contains("ActiveRegionTick", StringComparison.Ordinal));

        await Assert.That(activeRegion).IsNotNull();
        await Assert.That(activeRegion!.UseAsync).IsTrue();
    }

    #endregion

    #region Helper Methods

    private static WorldManager CreateWorldManager()
    {
        var mockTickManager = Mock.Of<ITickManager>();
        var mockWorldIdManager = Mock.Of<IWorldIdManager>();
        var mockZoneManager = Mock.Of<IZoneManager>();
        var mockIndunManager = Mock.Of<IIndunManager>();
        var mockFamilyManager = Mock.Of<IFamilyManager>();

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