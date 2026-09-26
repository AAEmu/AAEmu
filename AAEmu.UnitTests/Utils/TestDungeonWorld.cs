using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

using AAEmu.Commons.Utils;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Utils;

/// <summary>
/// Builds the small slice of a live copy the difficulty paths need: a world, a Dungeon whose selection
/// state is wired up, and characters registered inside it.
/// </summary>
/// <remarks>
/// <see cref="Dungeon"/> has no parameterless ctor and its real one claims worlds from live managers, so
/// the instance is built uninitialized and only the fields the selection paths read are filled in —
/// the same approach <c>SysIndunIndexResolverTests</c> takes for a resolver-only copy. Setting
/// <c>ParentWorld</c> resolves the world through the WorldManager singleton, so callers install one with
/// <see cref="InstallWorldManager"/> for the duration of the test and <see cref="CreateWorld"/> registers
/// the world in it.
/// </remarks>
public static class TestDungeonWorld
{
    /// <summary>Installs a bare WorldManager as the singleton for the scope's lifetime.</summary>
    public static IDisposable InstallWorldManager() =>
        new SingletonScope<WorldManager>(new WorldManager(null, null, null, null, null));

    public static WorldInstance CreateWorld(uint worldId, uint channelId, params uint[] zoneKeys)
    {
        var template = new WorldTemplate
        {
            Name = "test_dungeon_world",
            ZoneKeys = zoneKeys.ToList(),
        };
        var world = new WorldInstance(template, channelId, dontFreeInstanceId: true, instanceId: worldId);
        Register(world);
        return world;
    }

    /// <summary>
    /// A world whose template declares real cell bounds, so <c>GetRegionByPos</c> can
    /// resolve a position to a region. Paths that need spatial lookups (region membership,
    /// <c>WorldManager.GetAround</c>) need this; the plain <see cref="CreateWorld"/> has
    /// no bounds and every region lookup returns null.
    /// </summary>
    public static WorldInstance CreateSizedWorld(
        uint worldId, uint channelId, int cellX, int cellY, params uint[] zoneKeys)
    {
        var sectorsPerSide = cellX * WorldManager.SECTORS_PER_CELL;
        var template = new WorldTemplate
        {
            Name = "test_sized_world",
            ZoneKeys = zoneKeys.ToList(),
            CellX = cellX,
            CellY = cellY,
        };
        // Both grids are sized from the template's cells during real content load; nothing
        // allocates them here, and a region lookup needs both.
        template.ZoneKeyByRegions = new uint[sectorsPerSide, cellY * WorldManager.SECTORS_PER_CELL];
        foreach (var zoneKey in zoneKeys)
        {
            for (var x = 0; x < sectorsPerSide; x++)
            {
                for (var y = 0; y < cellY * WorldManager.SECTORS_PER_CELL; y++)
                    template.ZoneKeyByRegions[x, y] = zoneKey;
            }
        }

        var world = new WorldInstance(template, channelId, dontFreeInstanceId: true, instanceId: worldId);
        world.Regions = new Region[sectorsPerSide, cellY * WorldManager.SECTORS_PER_CELL];
        Register(world);
        return world;
    }

    public static Dungeon CreateDungeon(IndunZone indunZone, WorldInstance world)
    {
        var dungeon = (Dungeon)RuntimeHelpers.GetUninitializedObject(typeof(Dungeon));
        SetField(dungeon, "_indunZone", indunZone);
        SetField(dungeon, "_difficultySelection", new IndunDifficultySelectionState());
        SetField(dungeon, "_lock", new object());
        dungeon.World = world;
        world.DungeonInstance = dungeon;
        return dungeon;
    }

    /// <summary>Puts an object (a doodad, a character) into the world without registering contents.</summary>
    public static void Attach(GameObject gameObject, WorldInstance world) =>
        gameObject.ParentWorld = world;

    /// <summary>Registers the character inside the world the way an entry would.</summary>
    public static void Enter(WorldInstance world, Character character)
    {
        character.ParentWorld = world;
        CharactersOf(world)[character.ObjId] = character;
    }

    public static ConcurrentDictionary<uint, Character> CharactersOf(WorldInstance world) =>
        (ConcurrentDictionary<uint, Character>)typeof(WorldInstance)
            .GetField("_characters", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(world)!;

    /// <summary>
    /// Adds the world to the installed WorldManager: <c>ParentWorld</c>'s setter re-resolves the world
    /// through <c>WorldManager.Instance.GetWorld</c>, so an unregistered world would be dropped again.
    /// </summary>
    private static void Register(WorldInstance world)
    {
        var instanceField = typeof(Singleton<WorldManager>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic);
        if (instanceField?.GetValue(null) is not WorldManager manager)
            throw new InvalidOperationException(
                "TestDungeonWorld.InstallWorldManager() must run before CreateWorld: ParentWorld resolves the world through the WorldManager singleton.");

        var worlds = (ConcurrentDictionary<uint, WorldInstance>)typeof(WorldManager)
            .GetField("_worlds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager)!;
        worlds[world.Id] = world;
    }

    private static void SetField(object owner, string name, object value) =>
        owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(owner, value);
}
