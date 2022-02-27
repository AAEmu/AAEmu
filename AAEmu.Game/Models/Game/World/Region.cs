using System;
using System.Collections.Generic;
using System.Threading;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Route;

using NLog;

namespace AAEmu.Game.Models.Game.World
{
    public class Region
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private readonly uint _worldId;
        private readonly object _objectsLock = new object();
        private GameObject[] _objects;
        private int _objectsSize, _charactersSize;
        private Region[] _neighbors;
        private int _playerCount;

        public int X { get; }
        public int Y { get; }
        public int Id => Y + (1024 * X);
        public uint ZoneKey { get; set; }

        public Region(uint worldId, int x, int y, uint zoneKey) 
        {
            _worldId = worldId;
            X = x;
            Y = y;
            ZoneKey = zoneKey;
        }

        public void AddObject(GameObject obj)
        {
            if (obj == null)
                return;
            lock (_objectsLock)
            {
                if (_objects == null)
                {
                    _objects = new GameObject[50];
                    _objectsSize = 0;
                }
                else if (_objectsSize >= _objects.Length)
                {
                    var temp = new GameObject[_objects.Length * 2];
                    Array.Copy(_objects, 0, temp, 0, _objectsSize);
                    _objects = temp;
                }

                _objects[_objectsSize] = obj;
                _objectsSize++;

                obj.Transform.WorldId = _worldId;
                var zoneId = WorldManager.Instance.GetZoneId(_worldId, obj.Transform.World.Position.X, obj.Transform.World.Position.Y);
                if (zoneId > 0)
                    obj.Transform.ZoneId = zoneId;

                if (obj is Character)
                {
                    _charactersSize++;
                    foreach (var region in GetNeighbors())
                    {
                        Interlocked.Increment(ref region._playerCount);
                    }
                }

            }
            // Show debug info to subscribed players
            if (obj.Transform._debugTrackers.Count > 0)
                foreach (var chr in obj.Transform._debugTrackers)
                    chr?.SendMessage("[{0}] {1} entered region ({2} {3})){4}",
                        DateTime.UtcNow.ToString("HH:mm:ss"), obj.ObjId, X, Y,
                        obj is BaseUnit bu ? " - " + bu.Name : "");
        }

        public void RemoveObject(GameObject obj) // TODO Нужно доделать =_+
        {
            if (obj == null)
                return;
            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                    return;

                if (_objectsSize > 1)
                {
                    var index = -1;
                    for (var i = 0; i < _objects.Length; i++)
                        if (_objects[i] == obj)
                        {
                            index = i;
                            break;
                        }

                    if (index > -1)
                    {
                        _objects[index] = _objects[_objectsSize - 1];
                        _objects[_objectsSize - 1] = null;
                        _objectsSize--;
                    }
                }
                else if (_objectsSize == 1 && _objects[0] == obj)
                {
                    _objects[0] = null;
                    _objects = null;
                    _objectsSize = 0;
                }

                if (obj is Character)
                {
                    _charactersSize--;
                    foreach (var region in GetNeighbors())
                    {
                        Interlocked.Decrement(ref region._playerCount);
                    }
                }
                
            }
            // Show debug info to subscribed players
            if (obj.Transform._debugTrackers.Count > 0)
                foreach (var chr in obj.Transform._debugTrackers)
                    chr?.SendMessage("[{0}] {1} left the region ({2} {3})){4}",
                        DateTime.UtcNow.ToString("HH:mm:ss"), obj.ObjId, X, Y,
                        obj is BaseUnit bu ? " - " + bu.Name : "");
        }

        public void AddToCharacters(GameObject obj)
        {
            if (_objects == null)
                return;

            // Show the player all the facilities in the region when he/she is added
            if (obj is Character objectAsCharacter)
            {
                var objectsInRegion = GetList(new List<GameObject>(), obj.ObjId);
                foreach (var go in objectsInRegion)
                {
                    // Ignore doodads here, as we have a special packet for those
                    if (go is Doodad doodad)
                    {
                        var unit = WorldManager.Instance.GetUnit(doodad.OwnerObjId);
                        doodad.FuncGroupId = doodad.GetFuncGroupId();  // Start phase
                        doodad.DoPhaseFuncs(unit, (int)doodad.FuncGroupId);
                        continue;
                    }

                    // turn on the motion of the visible NPC
                    if (go is Npc npc && npc.Ai != null) 
                        npc.Ai.ShouldTick = true;
                    
                    go.AddVisibleObject(objectAsCharacter);
                }
                
                // Handle Doodads separately with sets of SCDoodadsCreatedPacket
                var doodads = GetList(new List<Doodad>(), obj.ObjId).ToArray();
                for (var i = 0; i < doodads.Length; i += SCDoodadsCreatedPacket.MaxCountPerPacket)
                {
                    var count = doodads.Length - i;
                    var temp = new Doodad[count <= SCDoodadsCreatedPacket.MaxCountPerPacket ? count : SCDoodadsCreatedPacket.MaxCountPerPacket];
                    Array.Copy(doodads, i, temp, 0, temp.Length);
                    objectAsCharacter.SendPacket(new SCDoodadsCreatedPacket(temp));
                }
            }
            
            // show the object to all players in the region
            foreach (var characterInRegion in GetList(new List<Character>(), obj.ObjId))
            {
                obj.AddVisibleObject(characterInRegion);
            }
        }

        public void RemoveFromCharacters(GameObject obj)
        {
            if (_objects == null)
                return;

            // remove all visible objects in the region from the player
            if (obj is Character character1)
            {
                //var unitIds = GetListId<Unit>(new List<uint>(), obj.ObjId).ToArray();
                var unitIds = GetListId<Unit>(new List<uint>(), character1.ObjId).ToArray();
                var units = GetList(new List<Unit>(), character1.ObjId);
                foreach (var t in units)
                {
                    if (t is Npc npc && npc.Ai != null)
                    {
                        npc.Ai.ShouldTick = false;
                    }
                }

                for (var offset = 0; offset < unitIds.Length; offset += SCUnitsRemovedPacket.MaxCountPerPacket)
                {
                    var length = unitIds.Length - offset;
                    var temp = new uint[length > SCUnitsRemovedPacket.MaxCountPerPacket ? SCUnitsRemovedPacket.MaxCountPerPacket : length];
                    Array.Copy(unitIds, offset, temp, 0, temp.Length);
                    character1.SendPacket(new SCUnitsRemovedPacket(temp));
                }
                var doodadIds = GetListId<Doodad>(new List<uint>(), character1.ObjId).ToArray();
                for (var offset = 0; offset < doodadIds.Length; offset += SCDoodadsRemovedPacket.MaxCountPerPacket)
                {
                    var length = doodadIds.Length - offset;
                    var last = length <= SCDoodadsRemovedPacket.MaxCountPerPacket;
                    var temp = new uint[last ? length : SCDoodadsRemovedPacket.MaxCountPerPacket];
                    Array.Copy(doodadIds, offset, temp, 0, temp.Length);
                    character1.SendPacket(new SCDoodadsRemovedPacket(last, temp));
                }
                // TODO ... others types...
            }

            // remove the object from all players in the region
            foreach (var character in GetList(new List<Character>(), obj.ObjId))
            {
                obj.RemoveVisibleObject(character);
            }
        }

        public Region[] GetNeighbors()
        {
            //Will neighbor regions ever change?
            if (_neighbors == null)
            {
                _neighbors = WorldManager.Instance.GetNeighbors(_worldId, X, Y);
                return _neighbors;
            }
            else
            {
                return _neighbors;
            }
        }

        public bool AreNeighborsEmpty()
        {
            if (!IsEmpty())
                return false;
            foreach (var neighbor in GetNeighbors())
                if (!neighbor.IsEmpty())
                    return false;
            return true;
        }

        public bool IsEmpty()
        {
            return _charactersSize <= 0;
        }

        public bool HasPlayerActivity()
        {
            return _playerCount > 0;
        }

        public List<uint> GetObjectIdsList(List<uint> result, uint exclude)
        {
            GameObject[] temp;
            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                    return result;
                temp = new GameObject[_objectsSize];
                Array.Copy(_objects, 0, temp, 0, _objectsSize);
            }

            foreach (var obj in temp)
                if (obj.ObjId != exclude)
                    result.Add(obj.ObjId);
            return result;
        }

        public List<GameObject> GetObjectsList(List<GameObject> result, uint exclude)
        {
            GameObject[] temp;
            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                    return result;
                temp = new GameObject[_objectsSize];
                Array.Copy(_objects, 0, temp, 0, _objectsSize);
            }

            foreach (var obj in temp)
                if (obj != null && obj.ObjId != exclude)
                    result.Add(obj);
            return result;
        }

        public List<uint> GetListId<T>(List<uint> result, uint exclude) where T : class
        {
            GameObject[] temp;
            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                    return result;
                temp = new GameObject[_objectsSize];
                Array.Copy(_objects, 0, temp, 0, _objectsSize);
            }

            foreach (var obj in temp)
                if (obj is T && obj.ObjId != exclude)
                    result.Add(obj.ObjId);

            return result;
        }

        public List<T> GetList<T>(List<T> result, uint exclude) where T : class
        {
            GameObject[] temp;
            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                    return result;
                temp = new GameObject[_objectsSize];
                Array.Copy(_objects, 0, temp, 0, _objectsSize);
            }

            foreach (var obj in temp)
            {
                var item = obj as T;
                if (item != null && obj.ObjId != exclude)
                    result.Add(item);
            }

            return result;
        }

        public List<T> GetList<T>(List<T> result, uint exclude, float x, float y, float sqrad, bool useModelSize = false) where T : class
        {
            GameObject[] temp;
            lock (_objectsLock)
            {
                if (_objects == null || _objectsSize == 0)
                    return result;
                temp = new GameObject[_objectsSize];
                Array.Copy(_objects, 0, temp, 0, _objectsSize);
            }

            foreach (var obj in temp)
            {
                var item = obj as T;
                if (item == null || obj.ObjId == exclude)
                    continue;

                var finalrad = sqrad;
                if (useModelSize)
                    finalrad += (obj.ModelSize * obj.ModelSize);
                
                var dx = obj.Transform.World.Position.X - x;
                dx *= dx;
                if (dx > finalrad)
                    continue;
                var dy = obj.Transform.World.Position.Y - y;
                dy *= dy;
                if (dx + dy < finalrad)
                    result.Add(item);
            }

            return result;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            if (obj.GetType() != typeof(Region))
                return false;
            var other = (Region)obj;
            return other._worldId == _worldId && other.X == X && other.Y == Y;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_worldId, X, Y);
        }
    }
}
