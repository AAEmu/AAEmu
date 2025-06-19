using System.Collections.Concurrent;
using System.Diagnostics;
using System.Xml;

using AAEmu.Commons.Exceptions;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.IO;
using AAEmu.Game.Models;
using AAEmu.Game.Models.ClientData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class WorldManager : Singleton<WorldManager>, IWorldManager
{
    /// <summary>
    /// Default World and Instance ID that will be assigned to all Transforms as a Default value
    /// This is the TemplateId of "main_world"
    /// </summary> 
    public static uint DefaultWorldTemplateId { get; private set; } // This will get reset to its proper value when loading world data (which is usually 0)

    /// <summary>
    /// InstanceId of "main_world"
    /// </summary>
    public static uint DefaultInstanceId { get; set; } = 0;
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Flags if WorldManager has finished loading
    /// </summary>
    private bool _loaded;

    /// <summary>
    /// List of Templates by world names 
    /// </summary>
    public Dictionary<string, WorldTemplate> WorldTemplates { get; set; } = [];

    private Dictionary<uint, WorldTemplate> WorldTemplatesById { get; set; } = [];
    private List<string> WorldNames { get; set; } = [];

    /// <summary>
    /// List of world spawn locations
    /// </summary>
    private List<WorldSpawnLocation> WorldSpawnLookups { get; set; } = [];

    /// <summary>
    /// List of loaded world instances (instanceId, WorldInstance)
    /// </summary>
    private Dictionary<uint, WorldInstance> _worlds = [];

    /// <summary>
    /// WorldTemplateId by ZoneId list (zoneId, worldTemplateId)
    /// </summary>
    private Dictionary<uint, uint> _worldIdByZoneKey = [];

    /// <summary>
    /// ZoneId list by WorldTemplateId
    /// </summary>
    private Dictionary<uint, List<uint>> _zoneKeysByWorldId = [];

    /// <summary>
    /// WorldInteractionGroup by Id
    /// </summary>
    private Dictionary<uint, WorldInteractionGroup> _worldInteractionGroups = [];

    /// <summary>
    /// List of all Characters in the server
    /// </summary>
    private readonly ConcurrentDictionary<uint, Character> _characters = [];

    /// <summary>
    /// List of all AreaShapes
    /// </summary>
    private readonly ConcurrentDictionary<uint, AreaShape> _areaShapes = [];

    /// <summary>
    /// List of all IndunZones in this instance (only used for dungeons)
    /// </summary>
    private readonly ConcurrentDictionary<uint, IndunZone> _indunZones = [];

    /// <summary>
    /// Reference to main_world instance
    /// </summary>
    public WorldInstance MainWorld { get; set; }

    /// <summary>
    /// Flag to keep track is the global snowing effect is enabled
    /// </summary>
    public bool IsSnowing { get; set; }

    // ReSharper disable InconsistentNaming
    /// <summary>
    /// Cell size in meters
    /// </summary>
    public const int CELL_SIZE = 1024;

    /// <summary>
    /// Sector size in meters
    /// </summary>
    public const int REGION_SIZE = 64;

    /// <summary>
    /// Number of sectors in a cell
    /// </summary>
    public const int SECTORS_PER_CELL = CELL_SIZE / REGION_SIZE;

    /// <summary>
    /// Used heightmap resolution for a sector/region
    /// </summary>
    public const int SECTOR_HMAP_RESOLUTION = REGION_SIZE / 2;

    /// <summary>
    /// Used heightmap resolution for a cell
    /// </summary>
    public const int CELL_HMAP_RESOLUTION = CELL_SIZE / 2;

    /// <summary>
    /// REGION_NEIGHBORHOOD_SIZE (cell sector size) used for polling objects in your proximity
    /// Was originally set to 1, recommended 3 and max 5
    /// anything higher is overkill as you can't target it anymore in the client at that distance 
    /// </summary>
    public const sbyte REGION_NEIGHBORHOOD_SIZE = 2;
    // ReSharper enable InconsistentNaming

    /// <summary>
    /// Time in seconds before you are considered not in combat when doing no combat related actions
    /// </summary>
    public const float DefaultCombatTimeout = 15f;

    /// <summary>
    /// Called every second and forwards the tick to all live player related objects
    /// </summary>
    /// <param name="delta"></param>
    private void ActiveRegionTick(TimeSpan delta)
    {
        var sw = new Stopwatch();
        sw.Start();

        // Players
        foreach (var character in GetAllCharacters())
            character.OnActiveRegionTick(delta);

        foreach (var world in _worlds.Values)
        {
            // Pets
            foreach (var mate in world.GetAllMates())
                mate.OnActiveRegionTick(delta);

            // Vehicles
            foreach (var slave in world.GetAllSlaves())
                slave.OnActiveRegionTick(delta);

            var npcSpawners = world.SpawnManager.GetAllSpawners();

            // Фильтрация спавнеров
            if (sw.ElapsedMilliseconds > 50)
            {
                Logger.Debug($"Processed in world {world.Template.Name} {npcSpawners.Count} spawners...");
            }

            var activeSpawners = npcSpawners.Values.SelectMany(x => x)
                .Where(spawner => spawner.Template != null && IsSpawnerActive(spawner))
                .ToList();

            // Последовательная обработка спавнеров
            if (sw.ElapsedMilliseconds > 50)
            {
                Logger.Debug($"Processed {activeSpawners.Count} active spawners...");
            }

            foreach (var npcSpawner in activeSpawners)
            {
                npcSpawner.Update();
            }
        }

        sw.Stop();
        if (sw.ElapsedMilliseconds > 100)
        {
            Logger.Warn("ActiveRegionTick took {0}ms", sw.ElapsedMilliseconds);
        }
    }

    private bool IsSpawnerActive(NpcSpawner spawner)
    {
        return spawner.IsPlayerInSpawnRadius();
    }

    /// <summary>
    /// Gets a world interaction group
    /// </summary>
    /// <param name="worldInteractionType"></param>
    /// <returns></returns>
    public WorldInteractionGroup? GetWorldInteractionGroup(uint worldInteractionType)
    {
        return _worldInteractionGroups.TryGetValue(worldInteractionType, out var group) ? group : null;
    }

    /// <summary>
    /// Gets WorldTemplate by name 
    /// </summary>
    /// <param name="worldName"></param>
    /// <returns></returns>
    public WorldTemplate GetWorldTemplateByName(string worldName)
    {
        return WorldTemplates.GetValueOrDefault(worldName);
    }

    /// <summary>
    /// Gets world name by WorldTemplateId
    /// </summary>
    /// <param name="worldTemplateId"></param>
    /// <returns></returns>
    private string GetWorldName(uint worldTemplateId)
    {
        return WorldNames[(int)worldTemplateId];
    }

    /// <summary>
    /// Find a world template that has specified zone group in it
    /// </summary>
    /// <param name="zoneGroupId"></param>
    /// <returns></returns>
    public WorldTemplate GetWorldTemplateForZoneGroup(uint zoneGroupId)
    {
        foreach (var (_, worldTemplate) in WorldTemplates)
        {
            foreach (var zoneKey in worldTemplate.ZoneKeys)
            {
                var zone = ZoneManager.Instance.GetZoneByKey(zoneKey);
                if (zone.GroupId == zoneGroupId)
                    return worldTemplate;
            }
        }

        return null;
    }

    /// <summary>
    /// Loads all world templates from the game client
    /// </summary>
    /// <exception cref="OperationCanceledException"></exception>
    public void Load()
    {
        if (_loaded)
            return;

        _worlds = [];
        _worldIdByZoneKey = [];
        _worldInteractionGroups = [];
        _zoneKeysByWorldId = [];

        Logger.Info("Loading world data...");

        #region LoadClientData
        var worldXmlPaths = ClientFileManager.GetFilesInDirectory(Path.Combine("game", "worlds"), "world.xml", true);

        if (worldXmlPaths.Count <= 0)
        {
            throw new OperationCanceledException("No client worlds data has been found, please check the readme.txt file inside the ClientData folder for more info.");
        }

        WorldTemplates.Clear();
        WorldNames.Clear();
        WorldNames.Add("main_world");

        // Grab world_spawns.json info
        var spawnPositionFile = Path.Combine(FileManager.AppPath, "Data", "Worlds", "world_spawns.json");
        var contents = File.Exists(spawnPositionFile) ? File.ReadAllText(spawnPositionFile) : "";
        WorldSpawnLookups.Clear();
        if (string.IsNullOrWhiteSpace(contents))
            Logger.Error($"File {spawnPositionFile} doesn't exists or is empty.");
        else
            if (!JsonHelper.TryDeserializeObject(contents, out List<WorldSpawnLocation> worldSpawnLookupFromJson, out _))
            Logger.Error($"Error in {spawnPositionFile}.");
        else
            WorldSpawnLookups = worldSpawnLookupFromJson;

        // Add all instance names to the worldNames list to generate world template Ids
        foreach (var worldXmlPath in worldXmlPaths)
        {
            var worldName = Path.GetFileName(Path.GetDirectoryName(worldXmlPath)); // the base name of the current directory
            if (!WorldNames.Contains(worldName))
                WorldNames.Add(worldName);
        }

        // Load data for every instance name
        for (uint worldTemplateId = 0; worldTemplateId < WorldNames.Count; worldTemplateId++)
        {
            var worldName = GetWorldName(worldTemplateId);
            _ = CreateWorldTemplate(worldName);
        }
        #endregion

        #region LoadServerDB
        using (var connection = SQLite.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM wi_group_wis";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var id = reader.GetUInt32("wi_id");
                        var group = (WorldInteractionGroup)reader.GetUInt32("wi_group_id");
                        _worldInteractionGroups.Add(id, group);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM aoe_shapes";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var shape = new AreaShape
                        {
                            Id = reader.GetUInt32("id"),
                            Type = (AreaShapeType)reader.GetUInt32("kind_id"),
                            Value1 = reader.GetFloat("value1"),
                            Value2 = reader.GetFloat("value2"),
                            Value3 = reader.GetFloat("value3")
                        };
                        _areaShapes.TryAdd(shape.Id, shape);
                    }
                }
            }
        }
        #endregion

        _loaded = true;
    }

    public void Initialize()
    {
        TickManager.Instance.OnTick.Subscribe(ActiveRegionTick, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Create all the instances that should always exist in the server
    /// </summary>
    public void CreateStaticInstances()
    {
        // TODO: make this a config json

        // Erenor (main_world)
        MainWorld = CreateWorldInstance(GetWorldTemplateByName("main_world"), 0, true); // fixedInstanceId = 0

        // Then spawn the rest
        MainWorld.SpawnManager.SpawnAll();

        // Mirage Island
        // _ = IndunManager.Instance.CreateSystemInstance(null, GetWorldTemplateByName("arche_mall_world").ZoneKeys.First(), 0, true, 1);

        // Load initial instances according to config
        foreach (var dungeonLoadConfig in AppConfiguration.Instance.Dungeons.AutoCreate)
        {
            _ = IndunManager.Instance.CreateSystemInstance(null, GetWorldTemplateByName(dungeonLoadConfig.Name).ZoneKeys.First(), dungeonLoadConfig.Channel, true, dungeonLoadConfig.Id);
        }
    }

    /// <summary>
    /// Create a new World Instance
    /// </summary>
    /// <param name="worldTemplate">World template for this instance</param>
    /// <param name="channelId"></param>
    /// <param name="overrideInstanceId">Set true for static instances</param>
    /// <param name="fixedInstanceId">InstanceId to use if overrideInstanceId is set, must be lower than 100 (0x64)</param>
    /// <returns>Newly created instance, or the previously instance created instance if a static instance with the same name already exists</returns>
    public WorldInstance CreateWorldInstance(WorldTemplate worldTemplate, uint channelId, bool overrideInstanceId = false, uint fixedInstanceId = 0, Character notifyPlayer = null)
    {
        // Check if it's a Persistent single Instance like main_world
        // If it's marked as an instance or if it only has 1 zone defined, then it's a "dungeon"
        var canBeInstanced = worldTemplate.XmlWorld.IsInstance > 0 || worldTemplate.XmlWorld.Zones.Count <= 1;
        // TODO: Add code that if indun_zones.select_channel is set, that it is also a static instance 

        // If only one instance is allowed, check if it already exists, if it does, return that instead
        if (!canBeInstanced)
        {
            var previousWorld = _worlds.FirstOrDefault(w => w.Value.Template.Id == worldTemplate.Id).Value;
            if (previousWorld != null)
            {
                Logger.Warn($"Tried to create a new instance of {worldTemplate.Name} which does not allow multiple instances. Using InstanceId {previousWorld.Id} instead!");
                return previousWorld;
            }
        }

        if (fixedInstanceId > 100)
        {
            throw new GameException("Fixed Instance ID is too large");
        }

        // Create a new instance
        var world = new WorldInstance(worldTemplate, channelId, overrideInstanceId, overrideInstanceId ? fixedInstanceId : WorldIdManager.Instance.GetNextId());
        _worlds.Add(world.Id, world);

        notifyPlayer?.SendPacket(new SCProcessingInstancePacket((int)world.Template.ZoneKeys[0]));

        // Create the Instance regions
        var dx = world.Template.CellX * SECTORS_PER_CELL;
        var dy = world.Template.CellY * SECTORS_PER_CELL;
        world.Regions = new Region[dx, dy];
        for (var y = 0; y < dy; y++)
        {
            for (var x = 0; x < dx; x++)
            {
                world.Regions[x, y] = new Region(world, x, y, world.Template.ZoneKeys[0]);
            }
        }

        // Load water data
        world.LoadWaterBodies();

        // Create and start the actual physics engine
        world.StartPhysics();

        // Quest sphere handling instance
        world.SphereQuestManager = new SphereQuestManager(world);
        world.SphereQuestManager.Initialize();
        world.SphereQuestManager.Load();

        world.SpawnManager = new SpawnManager(world);
        world.SpawnManager.Load();

        world.GimmickManager = new GimmickManager(world);
        world.GimmickManager.Initialize();

        world.SlaveManager = new SlaveManager(world);
        world.SlaveManager.Initialize();

        world.MateManager = new MateManager(world);
        world.MateManager.Load();

        world.TransferManager = new TransferManager();
        world.TransferManager.Load();
        world.TransferManager.Initialize(); // starts tick

        // world.SpawnManager.SpawnAll();

        return world;
    }

    /// <summary>
    /// Loads WorldTemplate data from the client
    /// </summary>
    /// <param name="worldName"></param>
    /// <returns></returns>
    public WorldTemplate CreateWorldTemplate(string worldName)
    {
        var worldTemplateId = WorldNames.IndexOf(worldName);
        if (worldTemplateId == -1)
            return null; // instance name not defined

        var worldTemplate = GetWorldTemplateByName(worldName);
        if (worldTemplate != null)
            return worldTemplate;

        // Open XML file
        using var worldXmlData = ClientFileManager.GetFileStream(Path.Combine("game", "worlds", worldName, "world.xml"));
        var xml = new XmlDocument();
        xml.Load(worldXmlData);
        var worldNode = xml.SelectSingleNode("/World");
        if (worldNode == null)
        {
            // Couldn't find world XML?
            return null;
        }

        worldTemplate = new WorldTemplate { Id = (uint)worldTemplateId };
        worldTemplate.XmlWorld.ReadNode(worldNode, worldTemplate);

        worldTemplate.SpawnPosition = WorldSpawnLookups.FirstOrDefault(w => w.Name == worldTemplate.Name)?.SpawnPosition ?? new WorldSpawnPosition();
        worldTemplate.SpawnPosition.WorldId = worldTemplate.Id;

        // Add coordinates for zones
        foreach (var worldZones in worldTemplate.XmlWorldZones.Values)
        {
            foreach (var wsl in WorldSpawnLookups)
            {
                if (wsl.Name == worldZones.Name)
                {
                    worldZones.SpawnPosition = wsl.SpawnPosition;
                    worldZones.SpawnPosition.WorldId = worldTemplate.Id;
                    break;
                }
            }
        }

        WorldTemplates.Add(worldTemplate.Name, worldTemplate);
        WorldTemplatesById.Add(worldTemplate.Id, worldTemplate);

        // Cache zone keys to world reference
        foreach (var zoneKey in worldTemplate.ZoneKeys)
        {
            _worldIdByZoneKey.Add(zoneKey, worldTemplate.Id);

            if (!_zoneKeysByWorldId.ContainsKey(worldTemplate.Id))
                _zoneKeysByWorldId.Add(worldTemplate.Id, []);
            _zoneKeysByWorldId[worldTemplate.Id].Add(zoneKey);
        }

        // Mark "main_world" as the DefaultWorldId
        if (worldName == "main_world")
            DefaultWorldTemplateId = worldTemplate.Id; // prefer to do it like this, in case we change order or IDs later on

        return worldTemplate;
    }

    /// <summary>
    /// Loads heightmap data from hmap.dat files
    /// </summary>
    /// <param name="worldTemplate"></param>
    /// <returns></returns>
    private static bool LoadHeightMapFromDatFile(WorldTemplate worldTemplate)
    {
        var heightMap = Path.Combine(FileManager.AppPath, "Data", "Worlds", worldTemplate.Name, "hmap.dat");
        if (!File.Exists(heightMap))
        {
            Logger.Trace($"HeightMap for `{worldTemplate.Name}` not found");
            return false;
        }

        using (var stream = new FileStream(heightMap, FileMode.Open, FileAccess.Read, FileShare.None, 2 << 20))
        using (var br = new BinaryReader(stream))
        {
            var version = br.ReadInt32();
            if (version == 1)
            {
                var hMapCellX = br.ReadInt32();
                var hMapCellY = br.ReadInt32();
                br.ReadDouble(); // heightMaxCoefficient
                br.ReadInt32(); // count

                if (hMapCellX == worldTemplate.CellX && hMapCellY == worldTemplate.CellY)
                {
                    for (var cellX = 0; cellX < worldTemplate.CellX; cellX++)
                    {
                        for (var cellY = 0; cellY < worldTemplate.CellY; cellY++)
                        {
                            if (br.ReadBoolean())
                                continue;
                            for (var i = 0; i < SECTORS_PER_CELL; i++)
                                for (var j = 0; j < SECTORS_PER_CELL; j++)
                                    for (var x = 0; x < SECTOR_HMAP_RESOLUTION; x++)
                                        for (var y = 0; y < SECTOR_HMAP_RESOLUTION; y++)
                                        {
                                            var sx = cellX * CELL_HMAP_RESOLUTION + i * SECTOR_HMAP_RESOLUTION + x;
                                            var sy = cellY * CELL_HMAP_RESOLUTION + j * SECTOR_HMAP_RESOLUTION + y;

                                            worldTemplate.HeightMaps[sx, sy] = br.ReadUInt16();
                                        }
                        }
                    }
                }
                else
                {
                    Logger.Warn($"{worldTemplate.Name}: Invalid heightmap cells, does not match world definition ...");
                    return false;
                }
            }
            else
            {
                Logger.Warn($"{worldTemplate.Name}: Heightmap version not supported {version}");
                return false;
            }
        }

        Logger.Info($"{worldTemplate.Name} heightmap loaded");
        return true;
    }

    /// <summary>
    /// Load heightmap data from the game client data
    /// </summary>
    /// <param name="worldTemplate"></param>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    private static bool LoadHeightMapFromClientData(WorldTemplate worldTemplate)
    {
        // Use world.xml to check if we have client data enabled
        var worldXmlTest = Path.Combine("game", "worlds", worldTemplate.Name, "world.xml");
        if (!ClientFileManager.FileExists(worldXmlTest))
            return false;

        var version = VersionCalc.Draft;

        for (var cellY = 0; cellY < worldTemplate.CellY; cellY++)
            for (var cellX = 0; cellX < worldTemplate.CellX; cellX++)
            {
                var cellFileName = $"{cellX:000}_{cellY:000}";
                var heightMapFile = Path.Combine("game", "worlds", worldTemplate.Name, "cells", cellFileName, "client", "terrain", "heightmap.dat");
                if (ClientFileManager.FileExists(heightMapFile))
                {
                    using var stream = ClientFileManager.GetFileStream(heightMapFile);
                    if (stream == null)
                    {
                        //Logger.Trace($"Cell {cellFileName} not found or not used in {world.Name}");
                        continue;
                    }

                    // Read the cell hmap data
                    using var br = new BinaryReader(stream);
                    var hmap = new Hmap();

                    var disableReCalc = false; // (version == VersionCalc.V1) // Version is never VersionCalc.V1
                    // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                    if (hmap.Read(br, disableReCalc) < 0)
                    {
                        Logger.Error($"Error reading {heightMapFile}");
                        continue;
                    }

                    var nodes = hmap.Nodes
                        .OrderBy(cell => cell.BoxHeightmap.Min.X)
                        .ThenBy(cell => cell.BoxHeightmap.Min.Y)
                        .Where(x => x.pHMData.Length > 0)
                        .ToList();

                    // Read nodes into heightmap array

                    #region ReadNodes

                    for (ushort sectorX = 0; sectorX < SECTORS_PER_CELL; sectorX++) // 16x16 sectors / cell
                        for (ushort sectorY = 0; sectorY < SECTORS_PER_CELL; sectorY++)
                            for (ushort unitX = 0; unitX < SECTOR_HMAP_RESOLUTION; unitX++) // sector = 32x32 unit size
                                for (ushort unitY = 0; unitY < SECTOR_HMAP_RESOLUTION; unitY++)
                                {
                                    var node = nodes[sectorX * SECTORS_PER_CELL + sectorY];
                                    var oX = cellX * CELL_HMAP_RESOLUTION + sectorX * SECTOR_HMAP_RESOLUTION + unitX;
                                    var oY = cellY * CELL_HMAP_RESOLUTION + sectorY * SECTOR_HMAP_RESOLUTION + unitY;

                                    ushort value;
                                    switch (version)
                                    {
                                        // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
                                        case VersionCalc.V1:
                                            {
                                                var doubleValue = node.fRange * 100000d;
                                                var rawValue = node.RawDataByIndex(unitX, unitY);

                                                value = (ushort)((doubleValue / 1.52604335620711f) *
                                                                 worldTemplate.HeightMaxCoefficient /
                                                                 ushort.MaxValue * rawValue +
                                                                 node.BoxHeightmap.Min.Z * worldTemplate.HeightMaxCoefficient);
                                            }
                                            break;
                                        // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
                                        case VersionCalc.V2:
                                            {
                                                value = node.RawDataByIndex(unitX, unitY);
                                                /* var height */
                                                _ = node.RawDataToHeight(value);
                                            }
                                            break;
                                        case VersionCalc.Draft:
                                            {
                                                var height = node.GetHeight(unitX, unitY);
                                                value = (ushort)(height * worldTemplate.HeightMaxCoefficient);
                                            }
                                            break;
                                        default:
                                            throw new NotSupportedException(nameof(version));
                                    }

                                    worldTemplate.HeightMaps[oX, oY] = value;
                                }

                    #endregion
                }
            }

        Logger.Info($"{worldTemplate.Name} heightmap loaded");
        return true;
    }

    /// <summary>
    /// Load heightmaps for all world templates
    /// </summary>
    public void LoadHeightmaps()
    {
        if (AppConfiguration.Instance.HeightMapsEnable) // TODO fastboot if HeightMapsEnable = false!
        {
            Logger.Info("Loading heightmaps...");

            var loaded = 0;
            foreach (var worldTemplate in WorldTemplates.Values)
            {
                Logger.Info($"Loading heightmap of {worldTemplate.Name}");
                if (AppConfiguration.Instance.ClientData.PreferClientHeightMap && LoadHeightMapFromClientData(worldTemplate))
                    loaded++;
                else if (LoadHeightMapFromDatFile(worldTemplate))
                    loaded++;
                else if (LoadHeightMapFromClientData(worldTemplate))
                    loaded++;
            }

            Logger.Info($"Loaded {loaded}/{WorldTemplates.Count} heightmaps");
        }
    }

    /// <summary>
    /// Gets a world by it's instanceId
    /// </summary>
    /// <param name="worldInstanceId"></param>
    /// <returns></returns>
    public WorldInstance GetWorld(uint worldInstanceId)
    {
        if (_worlds.TryGetValue(worldInstanceId, out var res))
            return res;
        Logger.Fatal($"GetWorld: No such World Instance {worldInstanceId}");
        return null;
    }

    /// <summary>
    /// Get a list of all world instances
    /// </summary>
    /// <returns></returns>
    public WorldInstance[] GetWorlds()
    {
        return _worlds.Values.ToArray();
    }

    /// <summary>
    /// Get world template Id for a given zoneId
    /// </summary>
    /// <param name="zoneKey"></param>
    /// <returns></returns>
    public uint GetWorldIdByZoneKey(uint zoneKey)
    {
        if (_worldIdByZoneKey.TryGetValue(zoneKey, out var worldId))
            return worldId;
        Logger.Fatal($"GetWorldByZone: No world defined for ZoneId {zoneKey}");
        return uint.MaxValue; // -1
    }

    /// <summary>
    /// Get a world template for a given zoneId
    /// </summary>
    /// <param name="zoneKey"></param>
    /// <returns></returns>
    public WorldTemplate GetWorldTemplateByZoneKey(uint zoneKey)
    {
        if (_worldIdByZoneKey.TryGetValue(zoneKey, out var worldId))
            return GetWorldTemplateByName(GetWorldName(worldId));
        Logger.Fatal($"GetWorldByZone(): No world template defined for ZoneId {zoneKey}");
        return null;
    }

    /// <summary>
    /// Get zones for a given world template id
    /// </summary>
    /// <param name="worldId"></param>
    /// <returns></returns>
    public List<uint> GetZoneKeysByWorldId(uint worldId)
    {
        if (_zoneKeysByWorldId.TryGetValue(worldId, out var value))
            return value;
        return [];
    }

    /// <summary>
    /// Gets a zone Id for a given world template at target position
    /// </summary>
    /// <param name="worldTemplate"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public uint GetZoneId(WorldTemplate worldTemplate, float x, float y)
    {
        if (worldTemplate == null)
            return 0;

        var sx = (int)(x / REGION_SIZE);
        var sy = (int)(y / REGION_SIZE);

        if (!worldTemplate.ValidRegion(sx, sy))
        {
            Logger.Fatal($"GetZoneId: Coordinates out of bounds for WorldId {worldTemplate.Id} - x:{x:#,0.#} - y: {y:#,0.#}");
            return 0;
        }

        return worldTemplate.ZoneKeyByRegions[sx, sy];
    }

    /// <summary>
    /// Get "floor" height at a given position for a zone
    /// </summary>
    /// <param name="zoneKey">ZoneId used to find which world it needs to look in</param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public float GetHeight(uint zoneKey, float x, float y)
    {
        // try to find Z first in GeoData, and then in HeightMaps, if not found, leave Z as it is
        var height = 0f;
        var world = GetWorldTemplateByZoneKey(zoneKey);

        if (AppConfiguration.Instance.World.GeoDataMode && world.Id > 0)
        {
            var position = new WorldSpawnPosition { WorldId = 0, ZoneId = zoneKey, X = x, Y = y, Z = 0, Yaw = 0, Pitch = 0, Roll = 0 };
            height = AiGeoDataManager.Instance.GetHeight(zoneKey, position);
        }

        // check, as there is no geodata for main_world yet
        if (height == 0)
        {
            if (AppConfiguration.Instance.HeightMapsEnable)
            {
                try
                {
                    //var world = GetWorldByZone(zoneId);
                    height = world?.GetHeight(x, y) ?? 0f;
                }
                catch
                {
                    height = 0f;
                }
            }
        }

        return height;
    }

    /// <summary>
    /// Returns target height of World position of transform according to loaded heightmaps
    /// </summary>
    /// <param name="transform"></param>
    /// <returns>Height at target world transform, or transform.World.Position.Z if no heightmap could be found</returns>
    public float GetHeight(Transform transform)
    {
        // try to find Z first in GeoData, and then in HeightMaps, if not found, leave Z as it is
        var height = 0f;
        if (AppConfiguration.Instance.World.GeoDataMode && transform.WorldId > 0)
        {
            height = AiGeoDataManager.Instance.GetHeight(transform.ZoneId, transform.World.Position);
        }

        // check, as there is no geodata for main_world yet
        if (height == 0)
        {
            if (AppConfiguration.Instance.HeightMapsEnable)
            {
                try
                {
                    var world = GetWorld(transform.InstanceId);
                    height = world?.GetHeight(transform.World.Position.X, transform.World.Position.Y) ?? transform.World.Position.Z;
                }
                catch
                {
                    height = transform.World.Position.Z;
                }
            }
            else
            {
                height = transform.World.Position.Z;
            }
        }

        return height;
    }

    /// <summary>
    /// Gets the root GameObject all the way up from the parent/child object tree
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    private static GameObject GetRootObj(GameObject obj)
    {
        if (obj.ParentObj == null)
        {
            return obj;
        }
        else
        {
            return GetRootObj(obj.ParentObj);
        }
    }

    /// <summary>
    /// Gets the region a GameObject is in
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public Region GetRegion(GameObject obj)
    {
        obj = GetRootObj(obj);
        var world = obj.ParentWorld ?? GetWorld(obj.Transform.InstanceId);
        return world.GetRegionByPos(obj.Transform.World.Position);
    }

    /// <summary>
    /// Get a list of neighbouring Regions/Sectors as defined by REGION_NEIGHBORHOOD_SIZE
    /// </summary>
    /// <param name="world"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public Region[] GetNeighbors(WorldInstance world, int x, int y)
    {
        var result = new List<Region>();
        for (var a = -REGION_NEIGHBORHOOD_SIZE; a <= REGION_NEIGHBORHOOD_SIZE; a++)
            for (var b = -REGION_NEIGHBORHOOD_SIZE; b <= REGION_NEIGHBORHOOD_SIZE; b++)
                if (world.Template.ValidRegion(x + a, y + b) && world.Regions[x + a, y + b] != null)
                    result.Add(world.Regions[x + a, y + b]);

        return result.ToArray();
    }

    /// <summary>
    /// Gets an active Character by their names
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public Character GetCharacter(string name)
    {
        foreach (var player in _characters.Values)
            if (name.ToLower().Equals(player.Name.ToLower()))
                return player;
        return null;
    }

    /// <summary>
    /// Returns a player Character object based on the parameters.
    /// Priority is TargetName > CurrentTarget > character
    /// </summary>
    /// <param name="character">Source character</param>
    /// <param name="TargetName">Possible target name</param>
    /// <param name="FirstNonNameArgument">Returns 1 if TargetName was a valid online character, 0 otherwise</param>
    /// <returns></returns>
    public static Character GetTargetOrSelf(Character character, string TargetName, out int FirstNonNameArgument)
    {
        FirstNonNameArgument = 0;
        if (!string.IsNullOrWhiteSpace(TargetName))
        {
            var player = Instance.GetCharacter(TargetName);
            if (player != null)
            {
                FirstNonNameArgument = 1;
                return player;
            }
        }
        if (character.CurrentTarget is Character targetCharacter)
            return targetCharacter;
        return character;
    }

    /// <summary>
    /// Get an active Character by their ObjId
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Character GetCharacterByObjId(uint id)
    {
        return _characters.GetValueOrDefault(id);
    }

    /// <summary>
    /// Get an active Character by their database Id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Character GetCharacterById(uint id)
    {
        return _characters.Values.FirstOrDefault(player => player.Id.Equals(id));
    }

    /// <summary>
    /// Adds or updates a GameObject of its region object list
    /// </summary>
    /// <param name="obj"></param>
    public void AddVisibleObject(GameObject obj)
    {
        if (obj == null)
            return;
        var region = GetRegion(obj); // Get region of an Object or its Root object if it has one
        var currentRegion = obj.Region; // Current Region this object is in

        // If region didn't change, ignore
        if (region == null || currentRegion != null && currentRegion.Equals(region))
            return;

        if (currentRegion == null)
        {
            // If no currentRegion, add it (happens on new spawns)
            foreach (var neighbor in region.GetNeighbors())
                neighbor.AddToCharacters(obj);

            region.AddObject(obj);
            obj.Region = region;
        }
        else
        {
            // No longer in the same region, update things
            // Remove visibility from oldNeighbors
            var diffs = currentRegion.FindDifferenceBetweenRegions(region);
            if (diffs != null)
                foreach (var diff in diffs)
                    diff?.RemoveFromCharacters(obj);

            // Add visibility to newNeighbours
            diffs = region.FindDifferenceBetweenRegions(currentRegion);
            if (diffs != null)
                foreach (var diff in diffs)
                    if (obj.IsVisible)
                        diff?.AddToCharacters(obj);

            // Add this obj to the new region
            region.AddObject(obj);
            // Update its region
            obj.Region = region;

            // remove the obj from the old region
            currentRegion.RemoveObject(obj);
        }

        // Also show children
        if (obj.Transform?.Children?.Count > 0)
            foreach (var child in obj.Transform.Children)
                if (child != null)
                    AddVisibleObject(child.GameObject);

        //Logger.Warn($" objects={_objects.Count}, doodads={_doodads.Count}, npcs={_npcs.Count}, characters={_characters.Count}");
    }

    /// <summary>
    /// Removes a GameObject from its region object list
    /// </summary>
    /// <param name="obj"></param>
    public static void RemoveVisibleObject(GameObject obj)
    {
        if (obj?.Region == null)
            return;

        var neighbors = obj.Region.GetNeighbors();
        obj.Region?.RemoveObject(obj);

        if (neighbors == null)
            return;

        if (neighbors.Length > 0)
            foreach (var neighbor in neighbors)
                neighbor?.RemoveFromCharacters(obj);

        obj.Region = null;

        // Also remove children
        if (obj.Transform is null)
            return;

        if (obj.Transform.Children?.Count > 0)
            foreach (var child in obj.Transform.Children)
                if (child != null)
                    RemoveVisibleObject(child.GameObject);
    }

    /// <summary>
    /// Gets a list of all T GameObjects around a given GameObject depending on neighbourhood size
    /// </summary>
    /// <param name="obj"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static List<T> GetAround<T>(GameObject obj) where T : class
    {
        var result = new List<T>();
        if (obj?.Region == null)
            return result;

        foreach (var neighbor in obj.Region.GetNeighbors())
            neighbor?.GetList(result, obj.ObjId);

        return result;
    }

    /// <summary>
    /// Gets a list of all T GameObjects around a given radius around GameObject
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="radius">Checked radius only include objects is the neighbourhood size</param>
    /// <param name="useModelSize">Set true if model sizes are excluded from the distance check</param>
    /// <typeparam name="T"></typeparam>
    /// <returns>A list of GameObjects that meet the criteria</returns>
    public static List<T> GetAround<T>(GameObject obj, float radius, bool useModelSize = false) where T : class
    {
        var result = new List<T>();
        if (radius <= 0f)
            return result;
        if (obj?.Region == null)
            return result;

        if (useModelSize)
            radius += obj.ModelSize;

        if (radius > 0.0f && RadiusFitsCurrentRegion(obj, radius))
        {
            obj.Region.GetList(result, obj.ObjId, obj.Transform.World.Position.X, obj.Transform.World.Position.Y, radius * radius, useModelSize);
        }
        else
        {
            foreach (var neighbor in obj.Region.GetNeighbors())
                neighbor?.GetList(result, obj.ObjId, obj.Transform.World.Position.X, obj.Transform.World.Position.Y, radius * radius, useModelSize);
        }

        return result;
    }

    /// <summary>
    /// Gets a list of all T GameObjects within the target GameObject's neighbourhood
    /// </summary>
    /// <param name="obj"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    private static List<T> GetNeighborRegionsObjs<T>(GameObject obj) where T : class
    {
        var result = new List<T>();

        if (obj?.Region == null) return result;

        foreach (var neighbor in obj.Region.GetNeighbors())
            neighbor?.GetList(result, obj.ObjId);

        return result;
    }

    /// <summary>
    /// Checks if a radius around a GameObject is still always within the same Region/Sector
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="radius"></param>
    /// <returns></returns>
    private static bool RadiusFitsCurrentRegion(GameObject obj, float radius)
    {
        var xMod = obj?.Transform?.World?.Position.X % REGION_SIZE;
        if (xMod - radius < 0 || xMod + radius > REGION_SIZE)
            return false;

        var yMod = obj?.Transform?.World?.Position.Y % REGION_SIZE;
        if (yMod - radius < 0 || yMod + radius > REGION_SIZE)
            return false;
        return true;
    }

    /// <summary>
    /// Gets all T GameObjects around a GameObject using a given shape
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="shape"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static List<T> GetAroundByShape<T>(GameObject obj, AreaShape shape) where T : GameObject
    {
        switch (shape.Type)
        {
            case AreaShapeType.Sphere:
                {
                    var radius = shape.Value1 > 0 ? shape.Value1 : 40f;
                    return GetAround<T>(obj, radius, true);
                }
            case AreaShapeType.Cuboid:
                {
                    var diagonal = Math.Sqrt(shape.Value1 * shape.Value1 + shape.Value2 * shape.Value2);
                    var res = GetAround<T>(obj, (float)diagonal, true);
                    res = shape.ComputeCuboid(obj, res);
                    return res;
                }
            default:
                {
                    Logger.Error("AreaShape had impossible type");
                    //throw new ArgumentNullException(nameof(shape), "AreaShape type does not exist!");
                    break;
                }
        }

        return null;
    }

    /// <summary>
    /// Sends a packet to all players on the server
    /// </summary>
    /// <param name="packet"></param>
    public void BroadcastPacketToServer(GamePacket packet)
    {
        foreach (var character in _characters.Values)
        {
            character.SendPacket(packet);
        }
    }

    public void OnPlayerJoin(Character character)
    {
        // Turn snow on off 
        character.SendPacket(new SCOnOffSnowPacket(IsSnowing));

        // Family stuff
        if (character.Family > 0)
        {
            FamilyManager.Instance.OnCharacterLogin(character);
        }
    }

    public static void ResendVisibleObjectsToCharacter(Character character)
    {
        // Re-send visible flags to character getting out of cinema
        var stuffs = GetNeighborRegionsObjs<GameObject>(character);
        var doodads = new List<Doodad>();
        foreach (var stuff in stuffs)
        {
            if (stuff is Doodad d)
                doodads.Add(d);
            else
                stuff.AddVisibleObject(character);
        }

        for (var i = 0; i < doodads.Count; i += SCDoodadsCreatedPacket.MaxCountPerPacket)
        {
            var count = Math.Min(doodads.Count - i, SCDoodadsCreatedPacket.MaxCountPerPacket);
            var temp = doodads.GetRange(i, count).ToArray();
            character.SendPacket(new SCDoodadsCreatedPacket(temp));
        }
    }

    /// <summary>
    /// Gets a list of all characters on the server
    /// </summary>
    /// <returns></returns>
    public List<Character> GetAllCharacters()
    {
        return _characters.Values.ToList();
    }

    /// <summary>
    /// Gets a list of all Npcs on a given world instance
    /// </summary>
    /// <param name="worldInstanceId"></param>
    /// <returns></returns>
    public List<Npc> GetAllNpcsFromWorld(uint worldInstanceId)
    {
        return _worlds.TryGetValue(worldInstanceId, out var world) ? world.GetAllNpcs() : [];
    }

    /// <summary>
    /// Gets a list of all Doodads in a given world instance
    /// </summary>
    /// <param name="worldId"></param>
    /// <returns></returns>
    public List<Doodad> GetAllDoodadsFromWorld(uint worldId)
    {
        return _worlds.TryGetValue(worldId, out var world) ? world.GetAllDoodads() : [];
    }
    public List<Slave> GetAllSlavesFromWorld(uint worldId)
    {
        return _worlds.TryGetValue(worldId, out var world) ? world.GetAllSlaves() : [];
    }

    public AreaShape GetAreaShapeById(uint id)
    {
        return _areaShapes.GetValueOrDefault(id);
    }

    /// <summary>
    /// Stops all worlds from running
    /// </summary>
    public void Stop()
    {
        if (_worlds is not null)
        {
            foreach (var world in _worlds)
            {
                world.Value?.Physics?.Stop();
            }
        }
    }

    /// <summary>
    /// Removes a world instance
    /// </summary>
    /// <param name="worldInstanceId"></param>
    public void RemoveWorld(uint worldInstanceId)
    {
        if (!_worlds.Remove(worldInstanceId))
        {
            Logger.Info($"[Dungeon] couldn't remove the dungeon id={worldInstanceId}!");
        }
    }

    /// <summary>
    /// Adds a Character to the server list if it isn't already
    /// </summary>
    /// <param name="character"></param>
    /// <returns></returns>
    public bool TryAddCharacter(Character character)
    {
        return _characters.TryAdd(character.ObjId, character);
    }

    /// <summary>
    /// Tries to remove the Character from the server list
    /// </summary>
    /// <param name="playerObjId"></param>
    /// <returns></returns>
    public bool TryRemoveCharacter(uint playerObjId)
    {
        return _characters.TryRemove(playerObjId, out _);
    }
}
