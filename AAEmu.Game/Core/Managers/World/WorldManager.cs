using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

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
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Game.World.Xml;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class WorldManager : Singleton<WorldManager>, IWorldManager
{
    /// <summary>
    /// Default World and Instance ID that will be assigned to all Transforms as a Default value
    /// This is the TemplateId of "main_world"
    /// </summary> 
    public static uint DefaultWorldTemplateId { get; set; } // This will get reset to its proper value when loading world data (which is usually 0)

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
    public Dictionary<string, WorldTemplate> WorldTemplates { get; private set; } = new();
    public Dictionary<uint, WorldTemplate> WorldTemplatesById { get; private set; } = new();
    public List<string> WorldNames { get; private set; } = new();

    /// <summary>
    /// List of world spawn locations
    /// </summary>
    public List<WorldSpawnLocation> WorldSpawnLookups { get; private set; } = new();

    /// <summary>
    /// List of loaded world instances
    /// </summary>
    private Dictionary<uint, WorldInstance> _worlds;

    /// <summary>
    /// WorldTemplateId by ZoneId list (zoneId, worldTemplateId)
    /// </summary>
    private Dictionary<uint, uint> _worldIdByZoneId;

    /// <summary>
    /// ZoneId list by WorldTemplateId
    /// </summary>
    private Dictionary<uint, List<uint>> _zonesByWorldId;

    /// <summary>
    /// WorldInteractionGroup by Id
    /// </summary>
    private Dictionary<uint, WorldInteractionGroup> _worldInteractionGroups;

    /// <summary>
    /// List of all GameObjects in this instance
    /// </summary>
    private readonly ConcurrentDictionary<uint, GameObject> _objects = new();

    /// <summary>
    /// List of all BaseUnits in this instance
    /// </summary>
    private readonly ConcurrentDictionary<uint, BaseUnit> _baseUnits = new();

    /// <summary>
    /// List of all Units in this instance
    /// </summary>
    private readonly ConcurrentDictionary<uint, Unit> _units = new();

    /// <summary>
    /// List of all Doodads in this instance
    /// </summary>
    private readonly ConcurrentDictionary<uint, Doodad> _doodads = new();

    /// <summary>
    /// List of all Npcs in this instance
    /// </summary>
    private readonly ConcurrentDictionary<uint, Npc> _npcs = new();

    /// <summary>
    /// List of all Characters in this instance
    /// </summary>
    private readonly ConcurrentDictionary<uint, Character> _characters = new();

    /// <summary>
    /// List of all AreaShapes in this instance
    /// </summary>
    private readonly ConcurrentDictionary<uint, AreaShape> _areaShapes = new();

    /// <summary>
    /// List of all Transfers in this instance
    /// </summary>
    private readonly ConcurrentDictionary<uint, Transfer> _transfers = new();

    /// <summary>
    /// List of all Gimmicks in this instance
    /// </summary>
    private readonly ConcurrentDictionary<uint, Gimmick> _gimmicks = new();

    /// <summary>
    /// List of all Slaves in this instance
    /// </summary>
    private readonly ConcurrentDictionary<uint, Slave> _slaves = new();

    /// <summary>
    /// List of all Mates in this instance
    /// </summary>
    private readonly ConcurrentDictionary<uint, Mate> _mates = new();

    /// <summary>
    /// List of all IndunZones in this instance (only used for dungeons)
    /// </summary>
    private readonly ConcurrentDictionary<uint, IndunZone> _indunZones = new();

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
        // Players
        foreach (var character in GetAllCharacters())
            character.OnActiveRegionTick(delta);

        // Pets
        foreach (var mate in GetAllMates())
            mate.OnActiveRegionTick(delta);

        // Vehicles
        foreach (var slave in GetAllSlaves())
            slave.OnActiveRegionTick(delta);
    }

    /// <summary>
    /// Handle "is still in combat" related things
    /// </summary>
    /// <param name="unit"></param>
    private static void CombatTick(Unit unit)
    {
        // TODO: Make it so you can also become out of combat if you are not on any aggro lists
        if (unit.IsInBattle && unit.LastCombatActivity.AddSeconds(DefaultCombatTimeout) < DateTime.UtcNow)
        {
            unit.IsInBattle = false;
        }

        if ((unit is Character { IsInPostCast: true } character) && character.LastCast.AddSeconds(5) < DateTime.UtcNow)
        {
            character.IsInPostCast = false;
        }
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
    public string GetWorldName(uint worldTemplateId)
    {
        return WorldNames[(int)worldTemplateId];
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
        _worldIdByZoneId = [];
        _worldInteractionGroups = [];
        _zonesByWorldId = [];

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
                command.CommandText = "SELECT * FROM indun_zones";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var idz = new IndunZone()
                        {
                            ZoneGroupId = reader.GetUInt32("zone_group_id"),
                            Name = reader.GetString("name"),
                            Comment = reader.GetString("comment"),
                            LevelMin = reader.GetUInt32("level_min"),
                            LevelMax = reader.GetUInt32("level_max"),
                            MaxPlayers = reader.GetUInt32("max_players"),
                            PvP = reader.GetBoolean("pvp"),
                            HasGraveyard = reader.GetBoolean("has_graveyard"),
                            ItemId = reader.IsDBNull("item_id") ? 0 : reader.GetUInt32("item_id"),
                            RestoreItemTime = reader.GetUInt32("restore_item_time"),
                            PartyOnly = reader.GetBoolean("party_only"),
                            ClientDriven = reader.GetBoolean("client_driven"),
                            SelectChannel = reader.GetBoolean("select_channel")
                        };
                        idz.LocalizedName = LocalizationManager.Instance.Get("indun_zones", "name", idz.ZoneGroupId, idz.Name);
                        if (!_indunZones.TryAdd(idz.ZoneGroupId, idz))
                            Logger.Fatal($"Unable to add zone_group_id: {idz.ZoneGroupId} from indun_zone");
                    }
                }
            }

            Logger.Debug($"Loaded {_indunZones.Count} dungeon zones");

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

        TickManager.Instance.OnTick.Subscribe(ActiveRegionTick, TimeSpan.FromSeconds(1));

        _loaded = true;
    }

    public WorldInstance CreateWorldInstance(WorldTemplate worldTemplate)
    {
        // Check if it's a Persistent single Instance like main_world
        // If it's marked as an instance or if it only has 1 zone defined, then it's a "dungeon"
        var canBeInstanced = worldTemplate.XmlWorld.IsInstance > 0 || worldTemplate.XmlWorld.Zones.Count <= 1;
        // If only one instance is allowed, check if it already exists, if it does, return that instead
        if (!canBeInstanced)
        {
            var previousWorld = _worlds.FirstOrDefault(w => w.Value.Template.Id == worldTemplate.Id).Value;
            if (previousWorld != null)
                return previousWorld;
        }

        // Create a new instance
        var world = new WorldInstance { Template = worldTemplate };
        _worlds.Add((uint)_worlds.Count, world);

        // Create the Instance regions
        var dx =world.Template.CellX * SECTORS_PER_CELL;
        var dy = world.Template.CellY * SECTORS_PER_CELL;
        world.Regions = new Region[dx, dy];
        for (var y = 0; y < dy; y++)
        {
            for (var x = 0; x < dx; x++)
            {
                world.Regions[x, y] = new Region(world.Id, x, y, world.Template.ZoneKeys[0]);
            }
        }

        world.LoadWaterBodies();
        world.SphereQuestManager = new SphereQuestManager(world);
        world.SphereQuestManager.Initialize();
        world.SphereQuestManager.Load();
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
            _worldIdByZoneId.Add(zoneKey, worldTemplate.Id);

            if (!_zonesByWorldId.ContainsKey(worldTemplate.Id))
                _zonesByWorldId.Add(worldTemplate.Id, []);
            _zonesByWorldId[worldTemplate.Id].Add(zoneKey);
        }

        // Mark "main_world" as the DefaultWorldId
        if (worldName == "main_world")
            DefaultWorldTemplateId = worldTemplate.Id; // prefer to do it like this, in case we change order or IDs later on

        return worldTemplate;

    }

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
                    using (var stream = ClientFileManager.GetFileStream(heightMapFile))
                    {
                        if (stream == null)
                        {
                            //Logger.Trace($"Cell {cellFileName} not found or not used in {world.Name}");
                            continue;
                        }

                        // Read the cell hmap data
                        using (var br = new BinaryReader(stream))
                        {
                            var hmap = new Hmap();

                            var disableReCalc = false; // (version == VersionCalc.V1) // Version is never VersionCalc.V1
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
                                                case VersionCalc.V2:
                                                    {
                                                        value = node.RawDataByIndex(unitX, unitY);
                                                        /* var height */ _ = node.RawDataToHeight(value);
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
            }

        Logger.Info($"{worldTemplate.Name} heightmap loaded");
        return true;
    }

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

    public WorldInstance GetWorld(uint worldInstanceId)
    {
        if (_worlds.TryGetValue(worldInstanceId, out var res))
            return res;
        Logger.Fatal($"GetWorld(): No such World Instance {worldInstanceId}");
        return null;
    }

    public WorldInstance[] GetWorlds()
    {
        return _worlds.Values.ToArray();
    }

    public uint GetWorldIdByZone(uint zoneId)
    {
        if (_worldIdByZoneId.TryGetValue(zoneId, out var worldId))
            return worldId;
        Logger.Fatal($"GetWorldByZone(): No world defined for ZoneId {zoneId}");
        return 0xffffffff; // -1
    }
    public WorldTemplate GetWorldTemplateByZone(uint zoneId)
    {
        if (_worldIdByZoneId.TryGetValue(zoneId, out var worldId))
            return GetWorldTemplateByName(GetWorldName(worldId));
        Logger.Fatal($"GetWorldByZone(): No world template defined for ZoneId {zoneId}");
        return null;
    }

    public List<uint> GetZonesByWorldId(uint worldId)
    {
        if (_zonesByWorldId.TryGetValue(worldId, out var value))
            return value;
        return [];
    }

    public uint GetZoneId(uint worldTemplateId, float x, float y)
    {
        if (!WorldTemplatesById.TryGetValue(worldTemplateId, out var worldTemplate))
        {
            Logger.Fatal($"GetZoneId(): No such WorldId {worldTemplateId}");
            return 0;
        }
        var sx = (int)(x / REGION_SIZE);
        var sy = (int)(y / REGION_SIZE);

        if (!worldTemplate.ValidRegion(sx, sy))
        {
            Logger.Fatal($"GetZoneId(): Coordinates out of bounds for WorldId {worldTemplateId} - x:{x:#,0.#} - y: {y:#,0.#}");
            return 0;
        }

        return worldTemplate.ZoneKeyByRegions[sx, sy];
    }

    public float GetHeight(uint zoneId, float x, float y)
    {
        // try to find Z first in GeoData, and then in HeightMaps, if not found, leave Z as it is
        var height = 0f;
        var world = GetWorldTemplateByZone(zoneId);

        if (AppConfiguration.Instance.World.GeoDataMode && world.Id > 0)
        {
            var position = new WorldSpawnPosition { WorldId = 0, ZoneId = zoneId, X = x, Y = y, Z = 0, Yaw = 0, Pitch = 0, Roll = 0 };
            height = AiGeoDataManager.Instance.GetHeight(zoneId, position);
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
                    var world = GetWorld(transform.WorldId);
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

    public Region GetRegion(GameObject obj)
    {
        obj = GetRootObj(obj);
        var world = GetWorld(obj.Transform.WorldId);
        return GetRegion(world, obj.Transform.World.Position.X, obj.Transform.World.Position.Y);
    }

    public Region[] GetNeighbors(uint worldId, int x, int y)
    {
        var world = _worlds[worldId];

        var result = new List<Region>();
        for (var a = -REGION_NEIGHBORHOOD_SIZE; a <= REGION_NEIGHBORHOOD_SIZE; a++)
            for (var b = -REGION_NEIGHBORHOOD_SIZE; b <= REGION_NEIGHBORHOOD_SIZE; b++)
                if (ValidRegion(world.Id, x + a, y + b) && world.Regions[x + a, y + b] != null)
                    result.Add(world.Regions[x + a, y + b]);

        return result.ToArray();
    }

    public GameObject GetGameObject(uint objId)
    {
        return _objects.GetValueOrDefault(objId);
    }

    public BaseUnit GetBaseUnit(uint objId)
    {
        return _baseUnits.GetValueOrDefault(objId);
    }

    public Doodad GetDoodad(uint objId)
    {
        return _doodads.GetValueOrDefault(objId);
    }

    public Doodad GetDoodadByDbId(uint dbId)
    {
        var ret = _doodads.FirstOrDefault(x => x.Value.DbId == dbId).Value;
        return ret;
    }

    public List<Doodad> GetDoodadByHouseDbId(uint houseDbId)
    {
        var ret = _doodads.Where(x => x.Value.OwnerDbId == houseDbId).Select(y => y.Value).ToList();
        return ret;
    }

    /// <summary>
    /// Get Active Unit by ObjId
    /// </summary>
    /// <param name="objId"></param>
    /// <returns></returns>
    public Unit GetUnit(uint objId)
    {
        return _units.GetValueOrDefault(objId);
    }

    /// <summary>
    /// Get active NPC by ObjId
    /// </summary>
    /// <param name="objId"></param>
    /// <returns></returns>
    public Npc GetNpc(uint objId)
    {
        return _npcs.GetValueOrDefault(objId);
    }

    /// <summary>
    /// Gets the first active NPC with a specific TemplateId
    /// </summary>
    /// <param name="templateId"></param>
    /// <returns></returns>
    public Npc GetNpcByTemplateId(uint templateId)
    {
        return _npcs.Values.FirstOrDefault(x => x.TemplateId == templateId);
    }

    internal void SetNpc(uint objId, Npc npc)
    {
        _npcs[objId] = npc;
    }

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

    public Character GetCharacterByObjId(uint id)
    {
        _characters.TryGetValue(id, out var ret);
        return ret;
    }

    public Character GetCharacterById(uint id)
    {
        foreach (var player in _characters.Values)
            if (player.Id.Equals(id))
                return player;
        return null;
    }

    /// <summary>
    /// Adds a GameObject to the list of existing objects on the server
    /// </summary>
    /// <param name="obj"></param>
    public void AddObject(GameObject obj)
    {
        if (obj == null)
            return;

        _objects.TryAdd(obj.ObjId, obj);

        if (obj is BaseUnit baseUnit)
            _baseUnits.TryAdd(baseUnit.ObjId, baseUnit);
        if (obj is Unit unit)
            _units.TryAdd(unit.ObjId, unit);
        if (obj is Doodad doodad)
            _doodads.TryAdd(doodad.ObjId, doodad);
        if (obj is Npc npc)
            _npcs.TryAdd(npc.ObjId, npc);
        if (obj is Character character)
            _characters.TryAdd(character.ObjId, character);
        if (obj is Transfer transfer)
            _transfers.TryAdd(transfer.ObjId, transfer);
        if (obj is Gimmick gimmick)
            _gimmicks.TryAdd(gimmick.ObjId, gimmick);
        if (obj is Slave slave)
            _slaves.TryAdd(slave.ObjId, slave);
        if (obj is Mate mate)
            _mates.TryAdd(mate.ObjId, mate);
    }

    /// <summary>
    /// Removes a GameObject from the list of "existing" objects on the server
    /// </summary>
    /// <param name="objId"></param>
    /// <returns></returns>
    public bool RemoveObject(uint objId)
    {
        if (objId == 0)
            return false;

        var res = false;

        if (_objects.TryRemove(objId, out _))
        {
            Logger.Debug($"WorldManager: object {objId} removed from _objects");
            res = true;
        }

        if (_baseUnits.TryRemove(objId, out _))
        {
            Logger.Debug($"WorldManager: object {objId} removed from _baseUnits");
            res = true;
        }

        if (_units.TryRemove(objId, out _))
        {
            Logger.Debug($"WorldManager: object {objId} removed from _units");
            res = true;
        }

        if (_npcs.TryRemove(objId, out _))
        {
            Logger.Debug($"WorldManager: object {objId} removed from _npcs");
            res = true;
        }

        return res;
    }

    /// <summary>
    /// Removes a GameObject from the list of "existing" objects on the server
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public void RemoveObject(GameObject obj)
    {
        if (obj == null)
            return;

        _objects.TryRemove(obj.ObjId, out _);

        if (obj is BaseUnit)
            _baseUnits.TryRemove(obj.ObjId, out _);
        if (obj is Unit)
            _units.TryRemove(obj.ObjId, out _);
        if (obj is Doodad)
            _doodads.TryRemove(obj.ObjId, out _);
        if (obj is Npc)
            _npcs.TryRemove(obj.ObjId, out _);
        if (obj is Character)
            _characters.TryRemove(obj.ObjId, out _);
        if (obj is Transfer)
            _transfers.TryRemove(obj.ObjId, out _);
        if (obj is Gimmick)
            _gimmicks.TryRemove(obj.ObjId, out _);
        if (obj is Slave)
            _slaves.TryRemove(obj.ObjId, out _);
        if (obj is Mate mate)
            _mates.TryRemove(mate.ObjId, out _);
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

    public static List<T> GetAround<T>(GameObject obj) where T : class
    {
        var result = new List<T>();
        if (obj?.Region == null)
            return result;

        foreach (var neighbor in obj.Region.GetNeighbors())
            neighbor?.GetList(result, obj.ObjId);

        return result;
    }

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

    private static List<T> GetNeighborRegionsObjs<T>(GameObject obj) where T : class
    {
        var result = new List<T>();

        if (obj?.Region == null) return result;

        foreach (var neighbor in obj.Region.GetNeighbors())
            neighbor?.GetList(result, obj.ObjId);

        return result;
    }

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

    public List<T> GetInCell<T>(uint worldId, int x, int y) where T : class
    {
        var result = new List<T>();
        var regions = new List<Region>();
        for (var a = x * SECTORS_PER_CELL; a < (x + 1) * SECTORS_PER_CELL; a++)
            for (var b = y * SECTORS_PER_CELL; b < (y + 1) * SECTORS_PER_CELL; b++)
            {
                if (ValidRegion(worldId, a, b) && _worlds[worldId].Regions[a, b] != null)
                    regions.Add(_worlds[worldId].Regions[a, b]);
            }

        foreach (var region in regions)
            region.GetList(result, 0);
        return result;
    }

    public void BroadcastPacketToServer(GamePacket packet)
    {
        foreach (var character in _characters.Values)
        {
            character.SendPacket(packet);
        }
    }

    private static Region GetRegion(WorldInstance worldInstance, float x, float y)
    {
        var sx = (int)(x / REGION_SIZE);
        var sy = (int)(y / REGION_SIZE);
        return worldInstance.GetRegion(sx, sy);
    }

    private bool ValidRegion(uint worldTemplateId, int x, int y)
    {
        var world = GetWorldTemplateByName(GetWorldName(worldTemplateId));
        return world != null && world.ValidRegion(x, y);
    }

    public void OnPlayerJoin(Character character)
    {
        //turn snow on off 
        Snow(character);

        //family stuff
        if (character.Family > 0)
        {
            FamilyManager.Instance.OnCharacterLogin(character);
        }
    }

    public void Snow(Character character)
    {
        //send the char the packet
        character.SendPacket(new SCOnOffSnowPacket(IsSnowing));
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

    public List<Character> GetAllCharacters()
    {
        return _characters.Values.ToList();
    }

    public List<Npc> GetAllNpcs()
    {
        return _npcs.Values.ToList();
    }

    public List<Npc> GetAllNpcsFromWorld(uint worldId)
    {
        return _npcs.Values.Where(n => n.Transform.WorldId == worldId).ToList();
    }

    public List<Doodad> GetAllDoodadsFromWorld(uint worldId)
    {
        return _doodads.Values.Where(d => d.Transform.WorldId == worldId).ToList();
    }

    public List<Slave> GetAllSlaves()
    {
        return _slaves.Values.ToList();
    }

    public List<Mate> GetAllMates()
    {
        return _mates.Values.ToList();
    }

    public List<Doodad> GetAllDoodads()
    {
        return _doodads.Values.ToList();
    }

    public List<Gimmick> GetAllGimmicks()
    {
        return _gimmicks.Values.ToList();
    }

    public List<Slave> GetAllSlavesFromWorld(uint worldId)
    {
        return _slaves.Values.Where(n => n.Transform.WorldId == worldId).ToList();
    }

    public AreaShape GetAreaShapeById(uint id)
    {
        return _areaShapes.GetValueOrDefault(id);
    }

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

    public void StartPhysics()
    {
        foreach (var (_, world) in _worlds)
        {
            world.Physics = new BoatPhysicsManager
            {
                SimulationWorld = world
            };
            world.Physics.Initialize();
            world.Physics.StartPhysics();
        }
    }

    public WorldInstance CreateWorld(WorldInstance originalWorld)
    {
        if (originalWorld == null)
            return null;

        // Apply Data to world
        // ReSharper disable once UseObjectOrCollectionInitializer
        var newInstance = new WorldInstance { Template = originalWorld.Template };
        newInstance.Id = WorldIdManager.Instance.GetNextId();
        newInstance.Physics = originalWorld.Physics;  // TODO: copy is looped .CloneJson();
        newInstance.Physics.SimulationWorld.Id = newInstance.Id;
        newInstance.Water = originalWorld.Water; // TODO: .CloneJson();
        var dx = newInstance.Template.CellX * SECTORS_PER_CELL;
        var dy = newInstance.Template.CellY * SECTORS_PER_CELL;
        newInstance.Regions = new Region[dx, dy];
        for (var y = 0; y < dy; y++)
        {
            for (var x = 0; x < dx; x++)
            {
                newInstance.Regions[x, y] = new Region(newInstance.Id, x, y, originalWorld.Template.ZoneKeys[0]);
            }
        }

        newInstance.Physics.SimulationWorld.Regions = newInstance.Regions;
        //SpawnManager.Instance.CloneNpcEventSpawners((byte)originalWorld.TemplateId, (byte)newInstance.Id);

        _worlds.Add(newInstance.Id, newInstance);

        return newInstance;
    }

    public void RemoveWorld(uint worldId)
    {
        if (!_worlds.Remove(worldId))
        {
            Logger.Info($"[Dungeon] couldn't remove the dungeon id={worldId}!");
        }
        //if (!SpawnManager.Instance.RemoveNpcEventSpawners((byte)worldId))
        //{
        //    Logger.Info($"[Dungeon] could not delete the list of NpcEventSpawners for dungeon id={worldId}!");
        //}
    }

    /// <summary>
    /// Get a list of NPCs that have loot and are past the "make public" time
    /// </summary>
    /// <returns></returns>
    public HashSet<Npc> GetNpcsToMakePublicLooting()
    {
        HashSet<Npc> temp;
        lock (_npcs)
        {
            temp = [.. _npcs.Values];
        }

        var res = new HashSet<Npc>();
        foreach (var item in temp.Where(item => item.LootingContainer.CanMakePublic()))
            res.Add(item);
        return res;
    }

    /// <summary>
    /// Gets the world instance a GameObject is currently in
    /// </summary>
    /// <param name="gameObject"></param>
    /// <returns>WorldInstance OR the main_world's Instance or null if all else fails</returns>
    public WorldInstance GetWorldOfGameObject(GameObject gameObject)
    {
        return _worlds.GetValueOrDefault(gameObject?.Transform?.InstanceId ?? DefaultInstanceId);
    }
}
