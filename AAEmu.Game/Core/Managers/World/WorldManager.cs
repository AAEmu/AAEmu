using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.DB;
using AAEmu.Game.Core.Packets.G2C;
using NLog;
using InstanceWorld = AAEmu.Game.Models.Game.World.World;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Managers.World
{
    public class WorldManager : Singleton<WorldManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, InstanceWorld> _worlds;
        private Dictionary<uint, uint> _worldIdByZoneId;
        private Dictionary<uint, WorldInteractionGroup> _worldInteractionGroups;
        public bool IsSnowing = false;
        private readonly ConcurrentDictionary<uint, GameObject> _objects;
        private readonly ConcurrentDictionary<uint, BaseUnit> _baseUnits;
        private readonly ConcurrentDictionary<uint, Unit> _units;
        private readonly ConcurrentDictionary<uint, Doodad> _doodads;
        private readonly ConcurrentDictionary<uint, Npc> _npcs;
        private readonly ConcurrentDictionary<uint, Character> _characters;
        private readonly ConcurrentDictionary<uint, AreaShape> _areaShapes;

        public const int REGION_SIZE = 64;
        public const int CELL_SIZE = 1024 / REGION_SIZE;
        /*
        REGION_NEIGHBORHOOD_SIZE (cell sector size) used for polling objects in your proximity
        Was originally set to 1, recommended 3 and max 5
        anything higher is overkill as you can't target it anymore in the client at that distance
        */
        public const sbyte REGION_NEIGHBORHOOD_SIZE = 3;

        public WorldManager()
        {
            _objects = new ConcurrentDictionary<uint, GameObject>();
            _baseUnits = new ConcurrentDictionary<uint, BaseUnit>();
            _units = new ConcurrentDictionary<uint, Unit>();
            _doodads = new ConcurrentDictionary<uint, Doodad>();
            _npcs = new ConcurrentDictionary<uint, Npc>();
            _characters = new ConcurrentDictionary<uint, Character>();
            _areaShapes = new ConcurrentDictionary<uint, AreaShape>();
        }

        public WorldInteractionGroup? GetWorldInteractionGroup(uint worldInteractionType)
        {
            if (_worldInteractionGroups.ContainsKey(worldInteractionType))
                return _worldInteractionGroups[worldInteractionType];
            return null;
        }

        public void Load()
        {
            _worlds = new Dictionary<uint, InstanceWorld>();
            _worldIdByZoneId = new Dictionary<uint, uint>();
            _worldInteractionGroups = new Dictionary<uint, WorldInteractionGroup>();

            _log.Info("Loading world data...");

            #region FileManager

            var pathFile = $"{FileManager.AppPath}Data/worlds.json";
            var contents = FileManager.GetFileContents(pathFile);
            if (string.IsNullOrWhiteSpace(contents))
                throw new IOException($"File {pathFile} doesn't exists or is empty.");

            if (JsonHelper.TryDeserializeObject(contents, out List<InstanceWorld> worlds, out _))
            {
                foreach (var world in worlds)
                {
                    if (_worlds.ContainsKey(world.Id))
                        throw new Exception("WorldManager: there are duplicates in world ids");

                    world.Regions = new Region[world.CellX * CELL_SIZE, world.CellY * CELL_SIZE];
                    world.ZoneIds = new uint[world.CellX * CELL_SIZE, world.CellY * CELL_SIZE];
                    world.HeightMaps = new ushort[world.CellX * 512, world.CellY * 512];
                    world.HeightMaxCoefficient = ushort.MaxValue / (world.MaxHeight / 4.0);

                    _worlds.Add(world.Id, world);
                }
            }
            else
                throw new Exception($"WorldManager: Parse {pathFile} file");

            foreach (var world in _worlds.Values)
            {
                pathFile = $"{FileManager.AppPath}Data/Worlds/{world.Name}/zones.json";
                contents = FileManager.GetFileContents(pathFile);
                if (string.IsNullOrWhiteSpace(contents))
                    throw new IOException($"File {pathFile} doesn't exists or is empty.");

                if (JsonHelper.TryDeserializeObject(contents, out List<ZoneConfig> zones, out _))
                    foreach (var zone in zones)
                    {
                        _worldIdByZoneId.Add(zone.Id, world.Id);

                        foreach (var cell in zone.Cells)
                        {
                            var x = cell.X * CELL_SIZE;
                            var y = cell.Y * CELL_SIZE;
                            foreach (var sector in cell.Sectors)
                            {
                                var sx = x + sector.X;
                                var sy = y + sector.Y;
                                world.ZoneIds[sx, sy] = zone.Id;
                                world.Regions[sx, sy] = new Region(world.Id, sx, sy);
                            }
                        }
                    }
                else
                    throw new Exception($"WorldManager: Parse {pathFile} file");
            }

            if (AppConfiguration.Instance.HeightMapsEnable) // TODO fastboot if HeightMapsEnable = false!
            {
                _log.Info("Loading heightmaps...");

                foreach (var world in _worlds.Values)
                {
                    var heightMap = $"{FileManager.AppPath}Data/Worlds/{world.Name}/hmap.dat";
                    if (!File.Exists(heightMap))
                    {
                        _log.Warn($"HeightMap at `{world.Name}` doesn't exists");
                        continue;
                    }

                    using (var stream = new FileStream(heightMap, FileMode.Open, FileAccess.Read))
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
                                    for (var cellY = 0; cellY < world.CellY; cellY++)
                                    {
                                        if (br.ReadBoolean())
                                            continue;
                                        for (var i = 0; i < 16; i++)
                                            for (var j = 0; j < 16; j++)
                                                for (var x = 0; x < 32; x++)
                                                    for (var y = 0; y < 32; y++)
                                                    {
                                                        var sx = cellX * 512 + i * 32 + x;
                                                        var sy = cellY * 512 + j * 32 + y;

                                                        world.HeightMaps[sx, sy] = br.ReadUInt16();
                                                    }
                                    }
                            }
                            else
                                _log.Warn("{0}: Invalid heightmap cells...", world.Name);
                        }
                        else
                            _log.Warn("{0}: Heightmap not correct version", world.Name);
                    }

                    _log.Info("Heightmap {0} loaded", world.Name);
                }

                _log.Info("Heightmaps loaded");
            }

            #endregion

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
                            var shape = new AreaShape();
                            shape.Id = reader.GetUInt32("id");
                            shape.Type = (AreaShapeType)reader.GetUInt32("kind_id");
                            shape.Value1 = reader.GetDouble("value1");
                            shape.Value2 = reader.GetDouble("value2");
                            shape.Value3 = reader.GetDouble("value3");
                            _areaShapes.TryAdd(shape.Id, shape);
                        }
                    }
                }
            }
        }

        public InstanceWorld GetWorld(uint worldId)
        {
            return _worlds.ContainsKey(worldId) ? _worlds[worldId] : null;
        }

        public InstanceWorld[] GetWorlds()
        {
            return _worlds.Values.ToArray();
        }

        public InstanceWorld GetWorldByZone(uint zoneId)
        {
            return _worlds[_worldIdByZoneId[zoneId]];
        }

        public uint GetZoneId(uint worldId, float x, float y)
        {
            var world = _worlds[worldId];
            var sx = (int)(x / REGION_SIZE);
            var sy = (int)(y / REGION_SIZE);
            return world.ZoneIds[sx, sy];
        }

        public float GetHeight(uint zoneId, float x, float y)
        {
            try
            {
                var world = GetWorldByZone(zoneId);
                return world?.GetHeight(x, y) ?? 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private GameObject GetRootObj(GameObject obj)
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
            InstanceWorld world = GetWorld(obj.Position.WorldId);
            return GetRegion(world, obj.Position.X, obj.Position.Y);
        }

        public Region GetRegion(Point point)
        {
            return GetRegion(point.ZoneId, point.X, point.Y);
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

        public Unit GetUnit(uint objId)
        {
            _units.TryGetValue(objId, out var ret);
            return ret;
        }

        public Npc GetNpc(uint objId)
        {
            _npcs.TryGetValue(objId, out var ret);
            return ret;
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
        public Character GetTargetOrSelf(Character character, string TargetName, out int FirstNonNameArgument)
        {
            FirstNonNameArgument = 0;
            if ((TargetName != null) && (TargetName != string.Empty))
            {
                Character player = WorldManager.Instance.GetCharacter(TargetName);
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
        }

        public void AddVisibleObject(GameObject obj)
        {
            if (obj == null || !obj.IsVisible)
                return;

            var region = GetRegion(obj);
            var currentRegion = obj.Region;

            if (region == null || currentRegion != null && currentRegion.Equals(region))
                return;

            if (currentRegion == null)
            {
                foreach (var neighbor in region.GetNeighbors())
                    neighbor.AddToCharacters(obj);

                region.AddObject(obj);
                obj.Region = region;
            }
            else
            {
                var oldNeighbors = currentRegion.GetNeighbors();
                var newNeighbors = region.GetNeighbors();

                foreach (var neighbor in oldNeighbors)
                {
                    var remove = true;
                    foreach (var newNeighbor in newNeighbors)
                        if (newNeighbor.Equals(neighbor))
                        {
                            remove = false;
                            break;
                        }

                    if (remove)
                        neighbor.RemoveFromCharacters(obj);
                }

                foreach (var neighbor in newNeighbors)
                {
                    var add = true;
                    foreach (var oldNeighbor in oldNeighbors)
                        if (oldNeighbor.Equals(neighbor))
                        {
                            add = false;
                            break;
                        }

                    if (add)
                        neighbor.AddToCharacters(obj);
                }

                region.AddObject(obj);
                obj.Region = region;

                currentRegion.RemoveObject(obj);
            }
        }

        public void RemoveVisibleObject(GameObject obj)
        {
            if (obj?.Region == null)
                return;

            obj.Region.RemoveObject(obj);

            foreach (var neighbor in obj.Region.GetNeighbors())
                neighbor.RemoveFromCharacters(obj);

            obj.Region = null;
        }

        public List<T> GetAround<T>(GameObject obj) where T : class
        {
            var result = new List<T>();
            if (obj.Region == null)
                return result;

            foreach (var neighbor in obj.Region.GetNeighbors())
                neighbor.GetList(result, obj.ObjId);

            return result;
        }

        public List<T> GetAround<T>(GameObject obj, float radius) where T : class
        {
            var result = new List<T>();
            if (obj.Region == null)
                return result;

            foreach (var neighbor in obj.Region.GetNeighbors())
                neighbor.GetList(result, obj.ObjId, obj.Position.X, obj.Position.Y, radius * radius);

            return result;
        }

        public List<T> GetInCell<T>(uint worldId, int x, int y) where T : class
        {
            var result = new List<T>();
            var regions = new List<Region>();
            for (var a = x * CELL_SIZE; a < (x + 1) * CELL_SIZE; a++)
                for (var b = y * CELL_SIZE; b < (y + 1) * CELL_SIZE; b++)
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

        [Obsolete("Please use ChatManager.Instance.GetFactionChat(factionMotherId).SendPacker(packet) instead.")]
        public void BroadcastPacketToFaction(GamePacket packet, uint factionMotherId)
        {
            foreach (var character in _characters.Values)
            {
                if (character.Faction.MotherId != factionMotherId)
                    continue;
                character.SendPacket(packet);
            }
        }

        [Obsolete("Please use ChatManager.Instance.GetZoneChat(zoneKey).SendPacker(packet) instead.")]
        public void BroadcastPacketToZone(GamePacket packet, uint zoneKey)
        {
            // First find the zone group, so functions like /shout work in larger zones that use multiple zone keys
            var zone = ZoneManager.Instance.GetZoneByKey(zoneKey);
            var zoneGroupId = zone?.GroupId ?? 0;
            var validZones = ZoneManager.Instance.GetZoneKeysInZoneGroupById(zoneGroupId);
            foreach (var character in _characters.Values)
            {
                if (!validZones.Contains(character.Position.ZoneId))
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

        private Region GetRegion(uint zoneId, float x, float y)
        {
            var world = GetWorldByZone(zoneId);
            var sx = (int)(x / REGION_SIZE);
            var sy = (int)(y / REGION_SIZE);
            return world.GetRegion(sx, sy);
        }

        private Region GetRegion(InstanceWorld world, float x, float y)
        {
            var sx = (int)(x / REGION_SIZE);
            var sy = (int)(y / REGION_SIZE);
            return world.GetRegion(sx, sy);
        }

        private bool ValidRegion(uint worldId, int x, int y)
        {
            var world = _worlds[worldId];
            return world.ValidRegion(x, y);
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

        public void ResendVisibleObjectsToCharacter(Character character)
        {
            // Re-send visible flags to character getting out of cinema
            var stuffs = WorldManager.Instance.GetAround<Unit>(character, 1000f);
            foreach (var stuff in stuffs)
            {
                if (stuff is House)
                    character.SendPacket(new SCHouseStatePacket((House)stuff));
                else
                if (stuff is Unit)
                    character.SendPacket(new SCUnitStatePacket((Unit)stuff));
            }

            var doodads = WorldManager.Instance.GetAround<Doodad>(character, 1000f).ToArray();
            for (var i = 0; i < doodads.Length; i += 30)
            {
                var count = doodads.Length - i;
                var temp = new Doodad[count <= 30 ? count : 30];
                Array.Copy(doodads, i, temp, 0, temp.Length);
                character.SendPacket(new SCDoodadsCreatedPacket(temp));
            }
        }

        public List<Character> GetAllCharacters()
        {
            return _characters.Values.ToList();
        }
        
        public AreaShape GetAreaShapeById(uint id)
        {
            if (_areaShapes.TryGetValue(id, out AreaShape res))
                return res;
            return null;
        }
    }
}
