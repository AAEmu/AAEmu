using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
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

        public int X { get; }
        public int Y { get; }
        public int Id => Y + 1024 * X;

        public Region(uint worldId, int x, int y)
        {
            _worldId = worldId;
            X = x;
            Y = y;
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

                obj.Position.WorldId = _worldId;
                var zoneId = WorldManager.Instance.GetZoneId(_worldId, obj.Position.X, obj.Position.Y);
                if (zoneId > 0)
                    obj.Position.ZoneId = zoneId;

                if (obj is Character)
                    _charactersSize++;
            }
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
                    _charactersSize--;
            }
        }

        public void AddToCharacters(GameObject obj)
        {
            if (_objects == null)
                return;

            // show the player all the facilities in the region
            if (obj is Character character1)
            {
                var units = GetList(new List<Unit>(), obj.ObjId);
                foreach (var t in units)
                {
                    // turn on the motion of the visible NPC
                    if (t is Npc npc)
                    {
                        // exclude Training Scarecrow from move objects and others NPC
                        if (npc.TemplateId == 7512 || npc.TemplateId == 7513 || npc.TemplateId == 7511 ||
                            npc.TemplateId == 9129 || npc.TemplateId == 9449)
                        {
                            // do nothing for these NPCs.
                        }
                        else
                        // We're gonna get the NPC's favorites on the road.
                        // Nui Forest keeper Arthur
                        // We're gonna get the NPC's favorites on the road.
                        // Nui Forest keeper Arthur
                        if (npc.TemplateId == 11999)
                        {
                            if (!npc.IsInPatrol)
                            {
                                npc.IsInPatrol = true; // so as not to run the route a second time
                                var path = new Simulation(npc);
                                path.MoveFileName = @"NuiForestkeeperArthur"; // path file name
                                path.ReadPath();
                                path.GoToPath(npc, true);
                            }
                        }
                        else
                            // Nui Woodcutter Solace
                        if (npc.TemplateId == 12143)
                        {
                            if (!npc.IsInPatrol)
                            {
                                npc.IsInPatrol = true; // so as not to run the route a second time
                                var path = new Simulation(npc);
                                path.MoveFileName = @"NuiWoodcutterSolace"; // path file name
                                path.ReadPath();
                                path.GoToPath(npc, true);
                            }
                        }
                        else
                        //                    deer                         swimmers
                        if (npc.TemplateId == 4200 || npc.TemplateId == 13677 || npc.TemplateId == 13676)
                        {
                            if (npc.Patrol == null)
                            {
                                Patrol patrol = null;
                                var rnd = Rand.Next(0, 400);
                                if (rnd > 300)
                                {
                                    // NPCs are moving squarely
                                    var square = new Square { Interrupt = true, Loop = true, Abandon = false };
                                    square.Degree = 360; // (short)Rand.Next(180, 360); checking that they're not leaving their seats.
                                    patrol = square;
                                }
                                else if (rnd > 200)
                                {
                                    // NPCs are moving around in a circle
                                    patrol = new Circular { Interrupt = true, Loop = true, Abandon = false };
                                }
                                else if (rnd > 100)
                                {
                                    // NPC move along the weaving shuttle in the Y-axis.
                                    var quill = new QuillY { Interrupt = true, Loop = true, Abandon = false };
                                    quill.Degree = (short)Rand.Next(180, 360);
                                    patrol = quill;
                                }
                                else if (rnd <= 100)
                                {
                                    // NPC move along the weaving shuttle in the X-axis.
                                    var quill = new QuillX { Interrupt = true, Loop = true, Abandon = false };
                                    quill.Degree = (short)Rand.Next(180, 360);
                                    patrol = quill;
                                }
                                if (patrol != null)
                                {
                                    patrol.Pause(npc);
                                    npc.Patrol = patrol;
                                    npc.Patrol.LastPatrol = null;
                                    patrol.Recovery(npc);
                                }
                            }
                        }
                        else
                        // here the NPCs you can hunt, check that they are not protected by Guards
                        if (npc.Faction.GuardHelp == false)
                        {
                            if (npc.Patrol == null)
                            {
                                Patrol patrol = null;
                                var rnd = Rand.Next(0, 1000);
                                if (rnd > 700)
                                {
                                    // NPC stand still
                                    // turned it off because the NPCs are leaving their seats.
                                    npc.Patrol = null;
                                    // NPC is moving slowly
                                    //var stirring = new Stirring() { Interrupt = true, Loop = true, Abandon = false };
                                    //stirring.Degree = (short)Rand.Next(180, 360);
                                    //patrol = stirring;
                                }
                                else if (rnd > 600)
                                {
                                    // NPCs are moving squarely
                                    var square = new Square { Interrupt = true, Loop = true, Abandon = false };
                                    square.Degree = 360; // (short)Rand.Next(180, 360); checking that they're not leaving their seats.
                                    patrol = square;
                                }
                                else if (rnd > 500)
                                {
                                    // NPCs are moving around in a circle
                                    patrol = new Circular { Interrupt = true, Loop = true, Abandon = false };
                                }
                                else if (rnd > 400)
                                {
                                    // NPC stand still
                                    // turned it off because the NPCs are leaving their seats.
                                    npc.Patrol = null;
                                    // NPCs are jerking around
                                    //var jerky = new Jerky { Interrupt = true, Loop = true, Abandon = false };
                                    //jerky.Degree = (short)Rand.Next(180, 360);
                                    //patrol = jerky;
                                }
                                else if (rnd > 300)
                                {
                                    // NPC move along the weaving shuttle in the Y-axis.
                                    var quill = new QuillY { Interrupt = true, Loop = true, Abandon = false };
                                    quill.Degree = (short)Rand.Next(180, 360);
                                    patrol = quill;
                                }
                                else if (rnd > 200)
                                {
                                    // NPC move along the weaving shuttle in the X-axis.
                                    var quill = new QuillX { Interrupt = true, Loop = true, Abandon = false };
                                    quill.Degree = (short)Rand.Next(180, 360);
                                    patrol = quill;
                                }
                                else if (rnd <= 200) // the bulk of the NPC is in place to reduce server load
                                {
                                    // NPC stand still
                                    npc.Patrol = null;
                                }
                                if (patrol != null)
                                {
                                    patrol.Pause(npc);
                                    npc.Patrol = patrol;
                                    npc.Patrol.LastPatrol = patrol;
                                    patrol.Recovery(npc);
                                }
                            }
                        }
                        character1.SendPacket(new SCUnitStatePacket(npc));
                    }
                    else
                    {
                        
                        if (t is House house)
                        {
                            character1.SendPacket(new SCUnitStatePacket(t));
                            character1.SendPacket(new SCHouseStatePacket(house));
                        }
                        else if (t is Slave slave)
                        {
                            slave.AddVisibleObject(character1);
                        }
                        else if (t is Mount mount)
                        {
                            mount.AddVisibleObject(character1);
                        }
                        else
                        {
                            character1.SendPacket(new SCUnitStatePacket(t));
                        }
                    }
                }
                var doodads = GetList(new List<Doodad>(), obj.ObjId).ToArray();
                for (var i = 0; i < doodads.Length; i += 30)
                {
                    var count = doodads.Length - i;
                    var temp = new Doodad[count <= 30 ? count : 30];
                    Array.Copy(doodads, i, temp, 0, temp.Length);
                    character1.SendPacket(new SCDoodadsCreatedPacket(temp));
                }
            }
            // show the object to all players in the region
            foreach (var character in GetList(new List<Character>(), obj.ObjId))
            {
                obj.AddVisibleObject(character);
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
                    if (t is Npc npc)
                    {
                        if (npc.TemplateId == 11999)
                        {
                            // leave the NPC on the way
                        }
                        else
                        {
                            npc.Patrol = null; // Stop NPCs that players don 't see
                        }
                    }
                }

                for (var offset = 0; offset < unitIds.Length; offset += 500)
                {
                    var length = unitIds.Length - offset;
                    var temp = new uint[length > 500 ? 500 : length];
                    Array.Copy(unitIds, offset, temp, 0, temp.Length);
                    character1.SendPacket(new SCUnitsRemovedPacket(temp));
                }
                var doodadIds = GetListId<Doodad>(new List<uint>(), character1.ObjId).ToArray();
                for (var offset = 0; offset < doodadIds.Length; offset += 400)
                {
                    var length = doodadIds.Length - offset;
                    var last = length <= 400;
                    var temp = new uint[last ? length : 400];
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
            return WorldManager.Instance.GetNeighbors(_worldId, X, Y);
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
                
                var dx = obj.Position.X - x;
                dx *= dx;
                if (dx > finalrad)
                    continue;
                var dy = obj.Position.Y - y;
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
            var result = (int)_worldId;
            result = (result * 397) ^ X;
            result = (result * 397) ^ Y;
            return result;
        }
    }
}
