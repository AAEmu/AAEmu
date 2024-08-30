using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.XML;
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

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Core.Managers.World;

public class WorldManager : Singleton<WorldManager>, IWorldManager
{
    // Default World and Instance ID that will be assigned to all Transforms as a Default value
    public static uint DefaultWorldId { get; set; } = 0; // This will get reset to it's proper value when loading world data (which is usually 0)
    public static uint DefaultInstanceId { get; set; } = 0;
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded = false;

    private Dictionary<uint, InstanceWorld> _worlds;
    private Dictionary<uint, uint> _worldIdByZoneId;
    private Dictionary<uint, List<uint>> _zonesByWorldId;
    private Dictionary<uint, WorldInteractionGroup> _worldInteractionGroups;
    public bool IsSnowing = false;
    private readonly ConcurrentDictionary<uint, GameObject> _objects;
    private readonly ConcurrentDictionary<uint, BaseUnit> _baseUnits;
    private readonly ConcurrentDictionary<uint, Unit> _units;
    private readonly ConcurrentDictionary<uint, Doodad> _doodads;
    private readonly ConcurrentDictionary<uint, Npc> _npcs;
    private readonly ConcurrentDictionary<uint, Character> _characters;
    private readonly ConcurrentDictionary<uint, AreaShape> _areaShapes;
    private readonly ConcurrentDictionary<uint, Transfer> _transfers;
    private readonly ConcurrentDictionary<uint, Gimmick> _gimmicks;
    private readonly ConcurrentDictionary<uint, Slave> _slaves;
    private readonly ConcurrentDictionary<uint, Mate> _mates;
    private readonly ConcurrentDictionary<uint, IndunZone> _indunZones;

    public const int CELL_SIZE = 1024;
    /// <summary>
    /// Sector Size
    /// </summary>
    public const int REGION_SIZE = 64;
    public const int SECTORS_PER_CELL = CELL_SIZE / REGION_SIZE;
    public const int SECTOR_HMAP_RESOLUTION = REGION_SIZE / 2;
    public const int CELL_HMAP_RESOLUTION = CELL_SIZE / 2;

    /*
    REGION_NEIGHBORHOOD_SIZE (cell sector size) used for polling objects in your proximity
    Was originally set to 1, recommended 3 and max 5
    anything higher is overkill as you can't target it anymore in the client at that distance
    */
    public const sbyte REGION_NEIGHBORHOOD_SIZE = 2;

    public const float DefaultCombatTimeout = 15f;

    public WorldManager()
    {
        _objects = new ConcurrentDictionary<uint, GameObject>();
        _baseUnits = new ConcurrentDictionary<uint, BaseUnit>();
        _units = new ConcurrentDictionary<uint, Unit>();
        _doodads = new ConcurrentDictionary<uint, Doodad>();
        _npcs = new ConcurrentDictionary<uint, Npc>();
        _characters = new ConcurrentDictionary<uint, Character>();
        _areaShapes = new ConcurrentDictionary<uint, AreaShape>();
        _transfers = new ConcurrentDictionary<uint, Transfer>();
        _gimmicks = new ConcurrentDictionary<uint, Gimmick>();
        _slaves = new ConcurrentDictionary<uint, Slave>();
        _mates = new ConcurrentDictionary<uint, Mate>();
        _indunZones = new ConcurrentDictionary<uint, IndunZone>();
    }

    private void ActiveRegionTick(TimeSpan delta)
    {
        // Players
        foreach (var character in GetAllCharacters())
        {
            CombatTick(character);
            RegenTick(character);
            BreathTick(character);
        }

        // Pets
        foreach (var mate in GetAllMates())
        {
            CombatTick(mate);
            RegenTick(mate);
        }

        // Vehicles
        foreach (var slave in GetAllSlaves())
        {
            CombatTick(slave);
            RegenTick(slave);
        }

        /*
        //var sw = new Stopwatch();
        //sw.Start();
        var activeRegions = new HashSet<Region>();
        foreach (var world in _worlds.Values)
        {
            foreach (var region in world.Regions)
            {
                if (region == null)
                    continue;
                if (activeRegions.Contains(region))
                    continue;
                if (region.IsEmpty())
                    continue;
                foreach (var activeRegion in region.GetNeighbors())
                {
                    activeRegions.Add(activeRegion);
                    var units = activeRegion.GetList<Unit>(new(), 0);
                    foreach (var unit in units)
                    {
                        if (unit is not Character character) { continue; }
                        CombatTick(character);
                        RegenTick(character);
                        BreathTick(character);
                    }
                }
            }
        }
        //sw.Stop();
        //Logger.Warn("ActiveRegionTick took {0}ms", sw.ElapsedMilliseconds);
        */
    }

    /// <summary>
    /// Handle is still in combat related things
    /// </summary>
    /// <param name="unit"></param>
    private static void CombatTick(Unit unit)
    {
        // TODO: Make it so you can also become out of combat if you are not on any aggro lists
        if (unit.IsInBattle && unit.LastCombatActivity.AddSeconds(DefaultCombatTimeout) < DateTime.UtcNow)
        {
            unit.IsInBattle = false;
        }

        if ((unit is Character character) && (character.IsInPostCast && character.LastCast.AddSeconds(5) < DateTime.UtcNow))
        {
            character.IsInPostCast = false;
        }
    }

    /// <summary>
    /// Call regeneration function of the unit
    /// </summary>
    /// <param name="unit"></param>
    private static void RegenTick(Unit unit)
    {
        unit.Regenerate();
    }

    /// <summary>
    /// Handle player's Breath updates
    /// </summary>
    /// <param name="character"></param>
    private static void BreathTick(Character character)
    {
        if (character.IsDead || !character.IsUnderWater)
        {
            return;
        }

        character.DoChangeBreath();
    }

    public WorldInteractionGroup? GetWorldInteractionGroup(uint worldInteractionType)
    {
        if (_worldInteractionGroups.TryGetValue(worldInteractionType, out var group))
            return group;
        return null;
    }

    public void Load()
    {
        if (_loaded)
            return;

        _worlds = new Dictionary<uint, InstanceWorld>();
        _worldIdByZoneId = new Dictionary<uint, uint>();
        _worldInteractionGroups = new Dictionary<uint, WorldInteractionGroup>();
        _zonesByWorldId = new Dictionary<uint, List<uint>>();

        Logger.Info("Loading world data...");

        #region LoadClientData

        var worldXmlPaths = ClientFileManager.GetFilesInDirectory(Path.Combine("game", "worlds"), "world.xml", true);

        if (worldXmlPaths.Count <= 0)
        {
            throw new OperationCanceledException("No client worlds data has been found, please check the readme.txt file inside the ClientData folder for more info.");
        }
        var worldNames = new List<string>();
        worldNames.Add("main_world"); // Make sure main_world is the first even if it wouldn't exist

        // Grab world_spawns.json info
        var spawnPositionFile = Path.Combine(FileManager.AppPath, "Data", "Worlds", "world_spawns.json");
        var contents = File.Exists(spawnPositionFile) ? File.ReadAllText(spawnPositionFile) : "";
        var worldSpawnLookup = new List<WorldSpawnLocation>();
        if (string.IsNullOrWhiteSpace(contents))
            Logger.Error($"File {spawnPositionFile} doesn't exists or is empty.");
        else
            if (!JsonHelper.TryDeserializeObject(contents, out List<WorldSpawnLocation> worldSpawnLookupFromJson, out _))
            Logger.Error($"Error in {spawnPositionFile}.");
        else
            worldSpawnLookup = worldSpawnLookupFromJson;

        foreach (var worldXmlPath in worldXmlPaths)
        {
            var worldName = Path.GetFileName(Path.GetDirectoryName(worldXmlPath)); // the base name of the current directory
            if (!worldNames.Contains(worldName))
                worldNames.Add(worldName);
        }

        for (uint id = 0; id < worldNames.Count; id++)
        {
            var worldName = worldNames[(int)id];
            if (worldName == "main_world")
                DefaultWorldId = id; // prefer to do it like this, in case we change order or IDs later on

            using var worldXmlData = ClientFileManager.GetFileStream(Path.Combine("game", "worlds", worldName, "world.xml"));
            var xml = new XmlDocument();
            xml.Load(worldXmlData);
            var worldNode = xml.SelectSingleNode("/World");
            if (worldNode != null)
            {
                var xmlWorld = new XmlWorld();
                var world = new InstanceWorld();
                world.Id = id;
                world.TemplateId = id;
                xmlWorld.ReadNode(worldNode, world);
                world.SpawnPosition = worldSpawnLookup.FirstOrDefault(w => w.Name == world.Name)?.SpawnPosition ?? new WorldSpawnPosition();
                world.SpawnPosition.WorldId = id;
                // add coordinates for zones
                foreach (var worldZones in world.XmlWorldZones.Values)
                {
                    foreach (var wsl in worldSpawnLookup)
                    {
                        if (wsl.Name == worldZones.Name)
                        {
                            worldZones.SpawnPosition = wsl.SpawnPosition;
                            worldZones.SpawnPosition.WorldId = id;
                            break;
                        }
                    }
                }

                _worlds.Add(id, world);

                // cache zone keys to world reference
                foreach (var zoneKey in world.ZoneKeys)
                {
                    _worldIdByZoneId.Add(zoneKey, id);

                    if (!_zonesByWorldId.ContainsKey(id))
                        _zonesByWorldId.Add(world.Id, new List<uint>());
                    _zonesByWorldId[id].Add(zoneKey);
                }

                world.Water = new WaterBodies();
            }
        }

        #endregion

        #region LoadServerDB

        using (var connection2 = SQLite.CreateConnection("Data", "compact.server.table.sqlite3"))
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
                        var idz = new IndunZone();
                        idz.ZoneGroupId = reader.GetUInt32("zone_group_id");
                        idz.Name = reader.GetString("name");
                        //idz.Comment = reader.GetString("comment");
                        idz.LevelMin = reader.GetUInt32("level_min");
                        idz.LevelMax = reader.GetUInt32("level_max");
                        idz.MaxPlayers = reader.GetUInt32("max_players");
                        idz.PvP = reader.GetBoolean("pvp");
                        idz.HasGraveyard = reader.GetBoolean("has_graveyard");
                        idz.ItemId = reader.IsDBNull("item_id") ? 0 : reader.GetUInt32("item_id");
                        idz.RestoreItemTime = reader.GetUInt32("restore_item_time");
                        idz.PartyOnly = reader.GetBoolean("party_only");
                        idz.ClientDriven = reader.GetBoolean("client_driven");
                        idz.SelectChannel = reader.GetBoolean("select_channel");
                        idz.LocalizedName = LocalizationManager.Instance.Get("indun_zones", "name", idz.ZoneGroupId, idz.Name);

                        if (!_indunZones.TryAdd(idz.ZoneGroupId, idz))
                            Logger.Fatal($"Unable to add zone_group_id: {idz.ZoneGroupId} from indun_zone");
                    }
                }
            }

            Logger.Debug($"Loaded {_indunZones.Count} dungeon zones");
            /*
            // add dummy main world as ID 0
            if (!_indunZones.TryAdd(0, new IndunZone() { ZoneGroupId = 0, Name = "Main World", LocalizedName = "Erenor" }))
            {
                Logger.Fatal("Failed to add main world");
                return;
            }
            */

            using (var command = connection2.CreateCommand())
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
                        var shape = new AreaShape();
                        shape.Id = reader.GetUInt32("id");
                        shape.Type = (AreaShapeType)reader.GetUInt32("kind_id");
                        shape.Value1 = reader.GetFloat("value1");
                        shape.Value2 = reader.GetFloat("value2");
                        shape.Value3 = reader.GetFloat("value3");
                        _areaShapes.TryAdd(shape.Id, shape);
                    }
                }
            }
        }
        #endregion

        TickManager.Instance.OnTick.Subscribe(ActiveRegionTick, TimeSpan.FromSeconds(1));

        _loaded = true;
    }

    public static bool LoadHeightMapFromDatFile(InstanceWorld world)
    {
        var heightMap = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "hmap.dat");
        if (!File.Exists(heightMap))
        {
            Logger.Trace($"HeightMap for `{world.Name}` not found");
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
                br.ReadDouble(); // heightMaxCoeff
                br.ReadInt32(); // count

                if (hMapCellX == world.CellX && hMapCellY == world.CellY)
                {
                    for (var cellX = 0; cellX < world.CellX; cellX++)
                    {
                        for (var cellY = 0; cellY < world.CellY; cellY++)
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

                                            world.HeightMaps[sx, sy] = br.ReadUInt16();
                                        }
                        }
                    }
                }
                else
                {
                    Logger.Warn("{0}: Invalid heightmap cells, does not match world definition ...", world.Name);
                    return false;
                }
            }
            else
            {
                Logger.Warn("{0}: Heightmap version not supported {1}", world.Name, version);
                return false;
            }
        }

        Logger.Info("{0} heightmap loaded", world.Name);
        return true;
    }

    public static bool LoadHeightMapFromClientData(InstanceWorld world)
    {
        // Use world.xml to check if we have client data enabled
        var worldXmlTest = Path.Combine("game", "worlds", world.Name, "world.xml");
        if (!ClientFileManager.FileExists(worldXmlTest))
            return false;

        var version = VersionCalc.Draft;

        for (var cellY = 0; cellY < world.CellY; cellY++)
            for (var cellX = 0; cellX < world.CellX; cellX++)
            {
                var cellFileName = $"{cellX:000}_{cellY:000}";
                var heightMapFile = Path.Combine("game", "worlds", world.Name, "cells", cellFileName, "client", "terrain", "heightmap.dat");
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
                                                                 world.HeightMaxCoefficient /
                                                                 ushort.MaxValue * rawValue +
                                                                 node.BoxHeightmap.Min.Z * world.HeightMaxCoefficient);
                                            }
                                            break;
                                        case VersionCalc.V2:
                                            {
                                                value = node.RawDataByIndex(unitX, unitY);
                                                var height = node.RawDataToHeight(value);
                                            }
                                            break;
                                        case VersionCalc.Draft:
                                            {
                                                var height = node.GetHeight(unitX, unitY);
                                                value = (ushort)(height * world.HeightMaxCoefficient);
                                            }
                                            break;
                                        default:
                                            throw new NotSupportedException(nameof(version));
                                    }

                                    world.HeightMaps[oX, oY] = value;
                                }

                    #endregion
                }
            }

        Logger.Info("{0} heightmap loaded", world.Name);
        return true;
    }

    public void LoadHeightmaps()
    {
        if (AppConfiguration.Instance.HeightMapsEnable) // TODO fastboot if HeightMapsEnable = false!
        {
            Logger.Info("Loading heightmaps...");

            var loaded = 0;
            foreach (var world in _worlds.Values)
            {
                if (AppConfiguration.Instance.ClientData.PreferClientHeightMap && LoadHeightMapFromClientData(world))
                    loaded++;
                else if (LoadHeightMapFromDatFile(world))
                    loaded++;
                else if (LoadHeightMapFromClientData(world))
                    loaded++;
            }

            Logger.Info($"Loaded {loaded}/{_worlds.Count} heightmaps");
        }
    }

    public void LoadWaterBodies()
    {
        foreach (var world in _worlds.Values)
        {
            var loadFromClient = true;

            // Try to load from saved json data
            var customFile = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name, "water_bodies.json");
            if (File.Exists(customFile))
            {
                if (WaterBodies.Load(customFile, out var newWater))
                {
                    world.Water = newWater;
                    loadFromClient = false;
                }
            }

            // If no custom data could be found or loaded, then load from client's cell data
            if (loadFromClient)
                LoadWaterBodiesFromClientData(world);
        }
    }

    public InstanceWorld GetWorld(uint worldId)
    {
        if (_worlds.TryGetValue(worldId, out var res))
            return res;
        Logger.Fatal("GetWorld(): No such WorldId {0}", worldId);
        return null;
    }

    public InstanceWorld[] GetWorlds()
    {
        return _worlds.Values.ToArray();
    }

    public uint GetWorldIdByZone(uint zoneId)
    {
        if (_worldIdByZoneId.TryGetValue(zoneId, out var worldId))
            return worldId;
        Logger.Fatal("GetWorldByZone(): No world defined for ZoneId {0}", zoneId);
        return 0xffffffff; // -1
    }
    public InstanceWorld GetWorldByZone(uint zoneId)
    {
        if (_worldIdByZoneId.TryGetValue(zoneId, out var worldId))
            return GetWorld(worldId);
        Logger.Fatal("GetWorldByZone(): No world defined for ZoneId {0}", zoneId);
        return null;
    }

    public List<uint> GetZonesByWorldId(uint worldId)
    {
        if (_zonesByWorldId.ContainsKey(worldId))
            return _zonesByWorldId[worldId];
        return new List<uint>();
    }

    public uint GetZoneId(uint worldId, float x, float y)
    {
        if (!_worlds.TryGetValue(worldId, out var world))
        {
            Logger.Fatal("GetZoneId(): No such WorldId {0}", worldId);
            return 0;
        }
        var sx = (int)(x / REGION_SIZE);
        var sy = (int)(y / REGION_SIZE);

        if (!world.ValidRegion(sx, sy))
        {
            Logger.Fatal("GetZoneId(): Coordinates out of bounds for WorldId {0} - x:{1:#,0.#} - y: {2:#,0.#}", worldId, x, y);
            return 0;
        }

        var region = world.GetRegion(sx, sy);
        return region.ZoneKey;
    }

    public float GetHeight(uint zoneId, float x, float y)
    {
        // try to find Z first in GeoData, and then in HeightMaps, if not found, leave Z as it is
        var height = 0f;
        var world = GetWorldByZone(zoneId);

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
        _objects.TryGetValue(objId, out var ret);
        return ret;
    }

    public BaseUnit GetBaseUnit(uint objId)
    {
        _baseUnits.TryGetValue(objId, out var ret);
        return ret;
    }

    public Doodad GetDoodad(uint objId)
    {
        _doodads.TryGetValue(objId, out var ret);
        return ret;
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
        if ((TargetName != null) && (TargetName != string.Empty))
        {
            var player = Instance.GetCharacter(TargetName);
            if (player != null)
            {
                FirstNonNameArgument = 1;
                return player;
            }
        }
        if ((character.CurrentTarget != null) && (character.CurrentTarget is Character))
            return (Character)character.CurrentTarget;
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
    /// Get ChildSlave by ObjId
    /// </summary>
    /// <param name="objId"></param>
    /// <returns></returns>
    public List<Slave> GetChildSlave(uint objId)
    {
        var res = new List<Slave>();
        foreach (var slave in _slaves)
        {
            if (slave.Key == objId)
            {
                res.Add(slave.Value);
            }
        }
        return res;
    }
    public List<Slave> GetAttachedSlavesByObjId(uint objId)
    {
        var res = new List<Slave>();
        foreach (var slave in _slaves)
        {
            if (slave.Key == objId)
            {
                res.AddRange(slave.Value.AttachedSlaves);
            }
        }
        return res;
    }
    public List<Doodad> GetAttachedDoodadsByObjId(uint objId, uint templateId)
    {
        var res = new List<Doodad>();
        foreach (var slave in _slaves)
        {
            if (slave.Key == objId)
            {
                foreach (var doodad in slave.Value.AttachedDoodads)
                {
                    if (doodad.TemplateId == templateId)
                    {
                        res.Add(doodad);
                    }
                }
            }
        }
        return res;
    }

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

    public bool RemoveObject(uint ObjId)
    {
        if (ObjId == 0)
            return false;

        var res = false;

        if (_objects.TryRemove(ObjId, out _))
        {
            Logger.Debug($"WorldManager: object {ObjId} removed from _objects");
            res = true;
        }

        if (_baseUnits.TryRemove(ObjId, out _))
        {
            Logger.Debug($"WorldManager: object {ObjId} removed from _baseUnits");
            res = true;
        }

        if (_units.TryRemove(ObjId, out _))
        {
            Logger.Debug($"WorldManager: object {ObjId} removed from _units");
            res = true;
        }

        if (_npcs.TryRemove(ObjId, out _))
        {
            Logger.Debug($"WorldManager: object {ObjId} removed from _npcs");
            res = true;
        }

        //_doodads.TryRemove(ObjId, out _);
        //_characters.TryRemove(ObjId, out _);
        //_transfers.TryRemove(ObjId, out _);
        //_gimmicks.TryRemove(ObjId, out _);
        //_slaves.TryRemove(ObjId, out _);
        //_mates.TryRemove(mate.ObjId, out _);

        return res;
    }
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

    public void AddVisibleObject(GameObject obj)
    {
        if (obj == null)
            return;
        var region = GetRegion(obj); // Get region of Object or it's Root object if it has one
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
            // Update it's region
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
        if (shape.Value1 == 0 && shape.Value2 == 0 && shape.Value3 == 0)
            Logger.Warn("AreaShape with no size values was used");
        if (shape.Type == AreaShapeType.Sphere)
        {
            var radius = shape.Value1;
            var height = shape.Value2;
            return GetAround<T>(obj, radius, true);
        }

        if (shape.Type == AreaShapeType.Cuboid)
        {
            var diagonal = Math.Sqrt(shape.Value1 * shape.Value1 + shape.Value2 * shape.Value2);
            var res = GetAround<T>(obj, (float)diagonal, true);
            res = shape.ComputeCuboid(obj, res);
            return res;
        }

        Logger.Error("AreaShape had impossible type");
        throw new ArgumentNullException(nameof(shape), "AreaShape type does not exist!");
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

    [Obsolete("Please use ChatManager.Instance.GetNationChat(race).SendPacker(packet) instead.")]
    public void BroadcastPacketToNation(GamePacket packet, Race race)
    {
        var mRace = (((byte)race - 1) & 0xFC); // some bit magic that makes raceId into some kind of birth continent id
        foreach (var character in _characters.Values)
        {
            var cmRace = (((byte)character.Race - 1) & 0xFC);
            if (mRace != cmRace)
                continue;
            character.SendPacket(packet);
        }
    }

    public void BroadcastPacketToServer(GamePacket packet)
    {
        foreach (var character in _characters.Values)
        {
            character.SendPacket(packet);
        }
    }

    public Region GetRegion(uint zoneId, float x, float y)
    {
        var world = GetWorldByZone(zoneId);
        var sx = (int)(x / REGION_SIZE);
        var sy = (int)(y / REGION_SIZE);
        return world.GetRegion(sx, sy);
    }

    private static Region GetRegion(InstanceWorld world, float x, float y)
    {
        var sx = (int)(x / REGION_SIZE);
        var sy = (int)(y / REGION_SIZE);
        return world.GetRegion(sx, sy);
    }

    private bool ValidRegion(uint worldId, int x, int y)
    {
        var world = GetWorld(worldId);
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

        StartingFirstJourney(character);
    }

    private void StartingFirstJourney(Character character)
    {
        var questId = 0u;
        var sphereId = 0u;
        switch (character.Race)
        {
            case Race.Nuian: // Nuian
                questId = 6839;
                sphereId = 2321;
                break;
            case Race.Dwarf: // Dwarf
                questId = 5811;
                sphereId = 2613;
                break;
            case Race.Elf: // Elf
                questId = 6840;
                sphereId = 2322;
                break;
            case Race.Hariharan: // Hariharan
                questId = 6842;
                sphereId = 2324;
                break;
            case Race.Ferre: // Ferre
                questId = 6841;
                sphereId = 2323;
                break;
            case Race.Warborn: // Warborn
                questId = 8228;
                sphereId = 2600;
                break;
            case Race.Fairy:
                break;
            case Race.Returned:
                break;
        }
        // showing it once
        if (character.Updated - character.Created < TimeSpan.FromMinutes(1))
        {
            character.Quests.AddQuestFromSphere(questId, sphereId);
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
        var stuffs = GetAround<GameObject>(character, REGION_NEIGHBORHOOD_SIZE * REGION_SIZE * 2); // увеличим радиус видимостив 2 раза для Npc и Doodad после просмотра видео
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

    public List<Slave> GetAllSlavesFromWorld(uint worldId)
    {
        return _slaves.Values.Where(n => n.Transform.WorldId == worldId).ToList();
    }

    public AreaShape GetAreaShapeById(uint id)
    {
        if (_areaShapes.TryGetValue(id, out var res))
            return res;
        return null;
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
        foreach (var (key, world) in _worlds)
        {
            world.Physics = new BoatPhysicsManager();
            world.Physics.SimulationWorld = world;
            world.Physics.Initialize();
            world.Physics.StartPhysics();
        }
    }

    public static bool LoadWaterBodiesFromClientData(InstanceWorld world)
    {
        // Use world.xml to check if we have client data enabled
        var worldXmlTest = Path.Combine("game", "worlds", world.Name, "world.xml");
        if (!ClientFileManager.FileExists(worldXmlTest))
            return false;

        var bodiesLoaded = 0;

        // TODO: The data loaded here is incorrect !!!

        for (var cellY = 0; cellY < world.CellY; cellY++)
            for (var cellX = 0; cellX < world.CellX; cellX++)
            {
                var cellFileName = $"{cellX:000}_{cellY:000}";
                var entityFile = Path.Combine("game", "worlds", world.Name, "cells", cellFileName, "client", "entities.xml");
                if (ClientFileManager.FileExists(entityFile))
                {
                    var xmlString = ClientFileManager.GetFileAsString(entityFile);
                    var xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(xmlString);

                    var _allEntityBlocks = xmlDoc.SelectNodes("/Mission/Objects/Entity");
                    var cellPos = new Vector3(cellX * 1024, cellY * 1024, 0);

                    for (var i = 0; i < _allEntityBlocks?.Count; i++)
                    {
                        var block = _allEntityBlocks[i];
                        var attribs = XmlHelper.ReadNodeAttributes(block);

                        if (!attribs.TryGetValue("Name", out var entityName))
                            continue;

                        // Is this Entity named like a water body ?
                        // TODO: More sophisticated way of determining if it's water
                        var isWaterBody = entityName.Contains("_water") || entityName.Contains("_pond") || entityName.Contains("_lake") || entityName.Contains("_river");
                        if (isWaterBody == false)
                            continue;

                        if (attribs.TryGetValue("EntityClass", out var entityClass))
                        {
                            // Is it a AreaShape ?
                            if (entityClass == "AreaShape")
                            {
                                var areaBlock = block.SelectSingleNode("Area");
                                if (areaBlock == null)
                                    continue; // this shape has no area defined

                                // Create WaterBody here
                                var newWaterBodyArea = new WaterBodyArea(entityName);
                                newWaterBodyArea.Id = XmlHelper.ReadAttribute(attribs, "EntityId", 0u);
                                newWaterBodyArea.Guid = XmlHelper.ReadAttribute(attribs, "Guid", "");

                                var entityPosString = XmlHelper.ReadAttribute(attribs, "Pos", "0,0,0");
                                var areaPos = XmlHelper.StringToVector3(entityPosString);

                                // Read Area Data (height)
                                var areaAttribs = XmlHelper.ReadNodeAttributes(areaBlock);
                                newWaterBodyArea.Height = XmlHelper.ReadAttribute(areaAttribs, "Height", 0f);

                                // Get Points within the Area
                                var pointBlocks = areaBlock.SelectNodes("Points/Point");

                                var firstPos = Vector3.Zero;
                                for (var p = 0; p < pointBlocks.Count; p++)
                                {
                                    var pointAttribs = XmlHelper.ReadNodeAttributes(pointBlocks[p]);
                                    var pointPosString = XmlHelper.ReadAttribute(pointAttribs, "Pos", "0,0,0");
                                    var pointPos = XmlHelper.StringToVector3(pointPosString);
                                    var pos = cellPos + areaPos + pointPos;
                                    newWaterBodyArea.Points.Add(pos);
                                    if (p == 0)
                                        firstPos = pos;
                                }

                                if (pointBlocks.Count > 2)
                                    newWaterBodyArea.Points.Add(firstPos);

                                newWaterBodyArea.UpdateBounds();
                                world.Water.Areas.Add(newWaterBodyArea);
                                bodiesLoaded++;
                            }
                        }
                    }
                }
            }

        if (bodiesLoaded > 0)
            Logger.Info($"{bodiesLoaded} waters bodies loaded for {world.Name}");
        return true;
    }

    public InstanceWorld CreateWorld(InstanceWorld originalWorld)
    {
        if (originalWorld == null)
            return null;

        // Apply Data to world
        var newInstance = new InstanceWorld();
        newInstance.Id = WorldIdManager.Instance.GetNextId();
        newInstance.TemplateId = originalWorld.TemplateId;
        newInstance.Name = originalWorld.Name;
        newInstance.CellX = originalWorld.CellX;
        newInstance.CellY = originalWorld.CellY;
        newInstance.OceanLevel = originalWorld.OceanLevel;
        newInstance.MaxHeight = originalWorld.MaxHeight;
        newInstance.HeightMaxCoefficient = originalWorld.HeightMaxCoefficient;
        newInstance.SpawnPosition = originalWorld.SpawnPosition.Clone();
        newInstance.SpawnPosition.WorldId = newInstance.Id;
        newInstance.ZoneKeys = originalWorld.ZoneKeys;
        newInstance.HeightMaps = originalWorld.HeightMaps; // TODO слишком долго копирует, клиент дисконнектит .CloneJson();
        newInstance.XmlWorldZones = originalWorld.XmlWorldZones; // TODO копирование зацикливается
        newInstance.Physics = originalWorld.Physics;  // TODO копирование зацикливается .CloneJson();
        newInstance.Physics.SimulationWorld.Id = newInstance.Id;
        newInstance.Water = originalWorld.Water; // TODO .CloneJson();
        var dx = originalWorld.CellX * SECTORS_PER_CELL;
        var dy = originalWorld.CellY * SECTORS_PER_CELL;
        newInstance.Regions = new Region[dx, dy];
        for (var y = 0; y < dy; y++)
        {
            for (var x = 0; x < dx; x++)
            {
                newInstance.Regions[x, y] = new Region(newInstance.Id, x, y, originalWorld.ZoneKeys[0]);
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
}
