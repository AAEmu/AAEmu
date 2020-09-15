using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Xml;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class TransferManager : Singleton<TransferManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, TransferTemplate> _templates;
        private Dictionary<uint, Transfer> _activeTransfers;
        private Dictionary<byte, Dictionary<uint, List<TransferRoads>>> _transferRoads;

        public bool Exist(uint templateId)
        {
            return _templates.ContainsKey(templateId);
        }

        public List<Point> GetTransferPath(string namePath, uint zoneId, byte worldId = 1)
        {
            foreach (var (wid, transfers) in _transferRoads)
            {
                if (wid != worldId) { continue; }
                foreach (var (zid, transfer) in transfers)
                {
                    if (zid != zoneId) { continue; }
                    foreach (var path in transfer.Where(path => path.Name == namePath))
                    {
                        return path.Pos;
                    }
                }
            }
            return null;
        }
        public List<Point> GetTransferPath(string namePath, byte worldId = 1)
        {
            foreach (var (wid, transfers) in _transferRoads)
            {
                if (wid != worldId) { continue; }
                foreach (var (zid, transfer) in transfers)
                {
                    foreach (var path in transfer.Where(path => path.Name == namePath))
                    {
                        return path.Pos;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Получить список всех частей своего пути для транспорта
        /// </summary>
        /// <param name="worldId"></param>
        /// <returns></returns>
        private void GetOwnerPaths(byte worldId = 1)
        {
            foreach (var (id, transferTemplate) in _templates)
            {
                foreach (var transferPaths in transferTemplate.TransferPaths)
                {
                    foreach (var (wid, transfers) in _transferRoads)
                    {
                        if (wid != worldId) { continue; }
                        foreach (var (zid, transfer) in transfers)
                        {
                            foreach (var path in transfer.Where(path => path.Name == transferPaths.PathName))
                            {
                                var tmp = new TransferRoads()
                                {
                                    Name = path.Name,
                                    Type = path.Type,
                                    CellX = path.CellX,
                                    CellY = path.CellY,
                                    Pos = path.Pos
                                };
                                transferTemplate.TransferRoads.Add(tmp);
                            }
                        }
                    }
                }
            }
        }



        /// <summary>
        /// Поиск участка пути по позиции транспорта. Предположительно, что начало пути имеет отличный от нуля ScType
        /// </summary>
        /// <param name="position">позиция транспорта</param>
        /// <param name="worldId"></param>
        /// <param name="tolerance"></param>
        /// <param name="firstPoint">-1 - любая точка, 0 - ищем первую точку, 1 - ищем последнюю точку</param>
        /// <returns>возвращаем список точек участка пути</returns>
        public List<Point> GetTransferRoads(Point position, byte worldId = 1, float tolerance = 1.401298E-45f, int firstPoint = -1)
        {
            // сначала найдем путь, в котором есть ближайшая к нашей точки место
            foreach (var (wid, transfers) in _transferRoads)
            {
                int res;
                if (wid != worldId) { continue; }
                foreach (var (zid, transfer) in transfers)
                {
                    foreach (var path in transfer)
                    {
                        res = GetMinCheckPointFromRoutes(position, path.Pos, tolerance);
                        if (res == -1) { continue; }

                        _log.Warn("GetTransferPath: found a way to the desired point: ", path.Name);
                        if (res == 0 && firstPoint == 0) //проверяем первая ли точка
                        {
                            // ищем по первой точке пути
                            _log.Warn("GetTransferPath: found the first point of way: ", path.Name);
                            return path.Pos;
                        }
                        if (res == path.Pos.Count - 1 && firstPoint == 1) //проверяем последняя ли точка
                        {
                            // ищем по последней точке пути
                            _log.Warn("GetTransferPath: found the first point of way: ", path.Name);
                            return path.Pos;
                        }

                        if (path.Type == 0) // предположительно, что начало пути имеет отличный от нуля ScType
                        {
                            return path.Pos;
                        }
                    }
                }
            }
            return null;
        }
        public (int, List<Point>) FindTransferRoad(Point target, Dictionary<int, List<Point>> listPaths, float tolerance = 1.401298E-45f, int firstPoint = -1)
        {
            // сначала найдем участк пути, в котором есть ближайшая к нашей точки место
            bool found;
            foreach (var (idx, path) in listPaths)
            {
                switch (firstPoint)
                {
                    //проверяем по любой точке
                    case -1:
                        {
                            // ищем по любой точке пути
                            var res = GetMinCheckPointFromRoutes(target, path, tolerance);
                            if (res == -1) { continue; } // не нашли, далее

                            _log.Warn("FindTransferRoad: found the point #{0} of way...", res);
                            return (idx, path);
                        }
                    //проверяем первая ли точка
                    case 0:
                        {
                            // ищем по первой точке пути
                            found = ComparePoints(target, path[0], tolerance);
                            if (!found) { continue; } // не нашли, далее

                            _log.Warn("FindTransferRoad: found the first point of way...");
                            return (idx, path);
                        }
                    //проверяем последняя ли точка
                    case 1:
                        {
                            // ищем по последней точке пути
                            found = ComparePoints(target, path[path.Count - 1], tolerance);
                            if (!found) { continue; } // не нашли, далее

                            _log.Warn("FindTransferRoad: found the end point of way...");
                            return (idx, path);
                        }
                }
            }
            return (-1, null);
        }

        /// <summary>
        /// Поиск начала участка пути по позиции транспорта. Предположительно, что начало пути имеет отличный от нуля ScType
        /// </summary>
        /// <param name="position">позиция транспорта</param>
        /// <param name="worldId"></param>
        /// <param name="firstPoint">-1 - любая точка, 0 - ищем первую точку, 1 - ищем последнюю точку</param>
        /// <returns>возвращаем список точек начального участка пути</returns>
        public List<Point> GetFirstTransferFirstPath(Point position, byte worldId = 1, int firstPoint = -1)
        {
            // сначала найдем путь, в котором есть ближайшая к нашей точки место
            foreach (var (wid, transfers) in _transferRoads)
            {
                int res;
                if (wid != worldId) { continue; }
                foreach (var (zid, transfer) in transfers)
                {
                    foreach (var path in transfer)
                    {
                        res = GetMinCheckPointFromRoutes(position, path.Pos);
                        if (res == -1) { continue; }

                        _log.Warn("GetTransferPath: found a way to the desired point: ", path.Name);
                        if (res == 0 && firstPoint == 0) //проверяем первая ли точка
                        {
                            // ищем по первой точке пути
                            _log.Warn("GetTransferPath: found the first point of way: ", path.Name);
                            return path.Pos;
                        }
                        if (res == path.Pos.Count - 1 && firstPoint == 1) //проверяем последняя ли точка
                        {
                            // ищем по последней точке пути
                            _log.Warn("GetTransferPath: found the first point of way: ", path.Name);
                            return path.Pos;
                        }

                        if (path.Type > 0) // предположительно, что начало пути имеет отличный от нуля ScType
                        {
                            return path.Pos;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// По TemplateId повозки найдем все имена путей и тип этих путей
        /// </summary>
        /// <param name="templateId"></param>
        /// <param name="worldId"></param>
        /// <returns></returns>
        public (List<int>, List<string>, Dictionary<int, List<Point>>) GetAllTypesNamesPaths(uint templateId, byte worldId = 1)
        {
            //var listId = new List<uint>();
            //                  path.ScType
            var listTypes = new List<int>();
            //                  path.Name
            var listNames = new List<string>();
            //                             idx,  path.WorldPos
            var listPaths = new Dictionary<int, List<Point>>();

            int idx = 0;
            // ищем транспорт с нужным templateId
            foreach (var (id, transferTemplate) in _templates.Where(paths => paths.Key == templateId))
            {
                foreach (var transferPaths in transferTemplate.TransferPaths)
                {
                    foreach (var (wid, transfers) in _transferRoads)
                    {
                        if (wid != worldId) { continue; }
                        foreach (var (zid, transfer) in transfers)
                        {
                            foreach (var path in transfer.Where(path => path.Name == transferPaths.PathName))
                            {
                                //listId.Add(transferTemplate.Id);
                                listNames.Add(path.Name);
                                listTypes.Add(path.Type);
                                listPaths.Add(idx, path.Pos);
                                idx++;
                            }
                        }
                    }
                }
            }

            return (listTypes, listNames, listPaths);
        }

        public (Dictionary<uint, Dictionary<int, List<Point>>>, Dictionary<uint, Dictionary<int, string>>) GetAllTransferPath2(uint templateId, byte worldId = 1)
        {
            /*
             Точка должна быть недалеко от остановки для транспорта или рядом с башней причаливания дирижаблей.
             По идее дорога состоит из четырех маршрутов, два в одну сторону и два в обратную, но бывает что состоит
             из двух дорог, по одному пути в каждую сторону.
            */
            var Tolerance = 1.401298E-45f;    // Погрешность
            //                             templateId,      idx,      Path
            var allRoutes = new Dictionary<uint, Dictionary<int, List<Point>>>();
            //                             templateId,           idx, PathName
            var allRouteNames = new Dictionary<uint, Dictionary<int, string>>();
            //                         idx,      Path
            var routes = new Dictionary<int, List<Point>>();
            //                         idx,      PathName
            var nameRoutes = new Dictionary<int, string>();
            const int firstPoint = 0; // нужно искать по первой точке
            const int lastPoint = 1; // нужно искать по последней точке
            const int anyPoint = -1; // нужно искать по любой точке

            // узнаем для нашей повозки типы, имена и пути имеющихся дорог
            /*
                                path.ScType
            var listTypes = new List<int>();
                                path.Name
            var listNames = new List<string>();
                                           idx,  path.WorldPos
            var listPaths = new Dictionary<uint, List<Point>>();
            */
            var (listTypes, listNames, listPaths) = GetAllTypesNamesPaths(templateId);

            var cnt = 0;
            // ищем начальные пути дороги
            foreach (var (i, path) in listPaths)
            {
                if (listTypes[i] == 0) { continue; }

                // записываем первую часть пути, для каждой дороги у которой ScType не ноль, для требуемого templateId
                _log.Warn("GetAllTransferPath2> нашли начало пути: {0}, для транспорта: {1}, для дороги с индесксом: {2}", templateId, listNames[i], i);
                routes.TryAdd(cnt, path);   // начальный участок дороги 1
                nameRoutes.TryAdd(cnt, listNames[i]); // добавили имя для участка
                cnt++;
            }

            var idx = 1;
            for (var i = 0; i < cnt; i++)
            {
                var count = 0;
                var endPoint = routes[i][routes[i].Count - 1]; // взяли последнюю точку в пути
                foreach (var (j, path) in listPaths)
                {
                    // берем из текущего участка пути последнюю точку и ищем  в других по начальной точке, пробуем пропустить поиск себя
                    var (index, routesFindNext) = FindTransferRoad(endPoint, listPaths, 5f, firstPoint);
                    if (routesFindNext == null) { break; }

                    _log.Warn("GetAllTransferPath2> нашли продолжение пути: {0}, для транспорта: {1}, для дороги с индесксом: {2}", templateId, listNames[index], index);
                    routes.TryAdd(idx, routesFindNext); // добавили остальные участки пути
                    nameRoutes.TryAdd(idx, listNames[index]);
                    idx++;
                    count++;
                    endPoint = routesFindNext[routes[i].Count - 1]; // взяли последнюю точку в пути
                }
                //allRoutes.TryAdd(templateId, routes); // точки участков дорог
                //allRouteNames.TryAdd(templateId, nameRoutes); // имена участков дорог соответствующие для allRoutes

                if (count != 0) { continue; }

                idx = 1;
                endPoint = routes[i][routes[i].Count - 1]; // взяли последнюю точку в пути
                                                           //foreach (var (j, path) in listPaths)
                                                           //{
                var (index0, routesFindNext0) = FindTransferRoad(endPoint, listPaths, Tolerance, firstPoint);
                if (routesFindNext0 == null) { break; }

                _log.Warn("GetAllTransferPath2> нашли единственное продолжение пути: {0}, для транспорта: {1}, для дороги с индесксом: {2}", templateId, listNames[index0], index0);
                routes.TryAdd(idx, routesFindNext0); // добавили остальные участки пути
                nameRoutes.TryAdd(idx, listNames[index0]);
                idx++;
                //}
            }
            allRoutes.TryAdd(templateId, routes); // точки участков дорог
            allRouteNames.TryAdd(templateId, nameRoutes); // имена участков дорог соответствующие для allRoutes
            return (allRoutes, allRouteNames);
        }

        public Dictionary<int, List<Point>> GetAllTransferPath(Point position, byte worldId = 1)
        {
            // точка должна быть недалеко от остановки для транспорта или рядом с башней причаливания дирижаблей
            // по идее дорога состоит из четырех маршрутов, два в одну сторону и два в обратную

            var routes = new Dictionary<int, List<Point>>();
            const int firstPoint = 0; // нужно искать по первой точке
            const int lastPoint = 1;  // нужно искать по последней точке
            const int anyPoint = -1;  // нужно искать по любой точке
            Point point;
            // сначала найдем путь, в котором есть ближайшая к нашей точки место
            var routesFindFirst = GetFirstTransferFirstPath(position, worldId, anyPoint);
            if (routesFindFirst == null)
            {
                // видимо надо поискать с конца
                var routesFindLast = GetTransferRoads(position, worldId, anyPoint);
                if (routesFindLast == null)
                {
                    _log.Warn("GetAllTransferPath: path not found.");
                    return null; // не нашли подходящего пути
                }
                routes.TryAdd(3, routesFindLast); // добавили последний путь
                point = routesFindLast[0]; // начальная точка пути
                for (var i = 2; i > -1; i--)
                {
                    var routesFindNext = GetTransferRoads(point, worldId, firstPoint);
                    if (routesFindNext == null)
                    { break; }

                    routes.TryAdd(i, routesFindNext); // добавили остальные пути
                    point = routesFindNext[0]; // начальная точка пути
                }
            }
            else
            {
                routes.TryAdd(0, routesFindFirst); // добавили первый путь
                point = routesFindFirst[routesFindFirst.Count - 1]; // конечная точка пути
                for (var i = 1; i < 4; i++)
                {
                    var routesFindNext = GetTransferRoads(point, worldId, lastPoint);
                    if (routesFindNext == null)
                    { break; }

                    routes.TryAdd(i, routesFindNext); // добавили остальные пути
                    point = routesFindNext[routesFindNext.Count - 1]; // конечная точка пути
                }
            }

            return routes;
        }
        public (int, int) GetMinCheckPointFromRoutes(Point position, Dictionary<int, List<Point>> routes)
        {
            var res = 0;
            var index = 0;
            for (var i = 0; i < routes.Count; i++)
            {
                res = GetMinCheckPointFromRoutes(position, routes[i]);
                if (res == -1) { continue; }

                index = i;
                break; // нашли нужную точку res в "пути" с индексом index
            }
            return (res, index);
        }
        //***************************************************************
        /// <summary>
        /// Ищем ближайшую точку у заданной, есть дополнительная проверка что точка таже самая или очень близкая
        /// </summary>
        /// <param name="position"></param>
        /// <param name="pointsList"></param>
        /// <param name="tolerance">дополнительная проверка что точка таже самая или очень близкая</param>
        /// <returns></returns>
        public int GetMinCheckPointFromRoutes(Point position, List<Point> pointsList, float tolerance = 1.401298E-45f)
        {
            //var Tolerance = 1.401298E-45f;    // Погрешность
            var index = -1;
            // check for a route
            if (pointsList.Count == 0)
            {
                _log.Warn("GetMinCheckPointFromRoutes: no route data.");
                return -1;
            }
            float delta;
            var minDist = 0f;
            var vTarget = new Vector3(position.X, position.Y, position.Z);
            for (var i = 0; i < pointsList.Count; i++)
            {
                var vPosition = new Vector3(pointsList[i].X, pointsList[i].Y, pointsList[i].Z);

                _log.Warn("#" + i + " x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);

                // проверка что это не таже самая точка
                delta = MathUtil.GetDistance(vTarget, vPosition);

                if (delta > tolerance && delta < 50) // ищем точку не очень далеко от позиции, проверяем, что это не таже самая точка
                {
                    if (index == -1) // first assignment
                    {
                        index = i;
                        minDist = delta;
                    }

                    if (delta < minDist) // save if less
                    {
                        index = i;
                        minDist = delta;
                    }
                }
            }

            return index;
        }
        public bool ComparePoints(Point target, Point position, float tolerance = 1.401298E-45f)
        {
            //var Tolerance = 1.401298E-45f;    // Погрешность
            var vTarget = new Vector3(target.X, target.Y, target.Z);
            var vPosition = new Vector3(position.X, position.Y, position.Z);

            _log.Warn("GetMinCheckPointFromRoutes> сравниваем точки...");
            _log.Warn("GetMinCheckPointFromRoutes> tarX:=" + vTarget.X + " tarY:=" + vTarget.Y + " tarZ:=" + vTarget.Z);
            _log.Warn("GetMinCheckPointFromRoutes> posX:=" + vPosition.X + " posY:=" + vPosition.Y + " posZ:=" + vPosition.Z);

            var delta = MathUtil.GetDistance(vTarget, vPosition);
            if (delta > tolerance && delta < 50) // ищем точку не очень далеко от позиции, проверяем, что это не таже самая точка
            {
                return true;
            }

            return false;
        }

        public void SpawnAll(Character character)
        {
            foreach (var tr in _activeTransfers.Values)
            {
                character.SendPacket(new SCUnitStatePacket(tr));
                character.SendPacket(new SCUnitPointsPacket(tr.ObjId, tr.Hp, tr.Mp));

                if (tr.AttachedDoodads.Count > 0)
                {
                    var doodads = tr.AttachedDoodads.ToArray();
                    for (var i = 0; i < doodads.Length; i += 30)
                    {
                        var count = doodads.Length - i;
                        var temp = new Doodad[count <= 30 ? count : 30];
                        Array.Copy(doodads, i, temp, 0, temp.Length);
                        character.SendPacket(new SCDoodadsCreatedPacket(temp));
                    }
                }
            }
        }
        public void Spawn(Character character, Transfer tr)
        {
            // спавним кабину
            character.SendPacket(new SCUnitStatePacket(tr));
            character.SendPacket(new SCUnitPointsPacket(tr.ObjId, tr.Hp, tr.Mp));

            // пробуем спавнить прицеп
            if (tr.Bounded != null)
            {
                character.SendPacket(new SCUnitStatePacket(tr.Bounded));
                character.SendPacket(new SCUnitPointsPacket(tr.Bounded.ObjId, tr.Bounded.Hp, tr.Bounded.Mp));

                if (tr.Bounded.AttachedDoodads.Count > 0)
                {

                    var doodads = tr.Bounded.AttachedDoodads.ToArray();
                    for (var i = 0; i < doodads.Length; i += 30)
                    {
                        var count = doodads.Length - i;
                        var temp = new Doodad[count <= 30 ? count : 30];
                        Array.Copy(doodads, i, temp, 0, temp.Length);
                        character.SendPacket(new SCDoodadsCreatedPacket(temp));
                    }
                }
            }

            // если есть Doodad в кабине
            if (tr.AttachedDoodads.Count > 0)
            {
                var doodads = tr.AttachedDoodads.ToArray();
                for (var i = 0; i < doodads.Length; i += 30)
                {
                    var count = doodads.Length - i;
                    var temp = new Doodad[count <= 30 ? count : 30];
                    Array.Copy(doodads, i, temp, 0, temp.Length);
                    character.SendPacket(new SCDoodadsCreatedPacket(temp));
                }
            }
        }

        public TransferTemplate GetTransferTemplate(uint id)
        {
            return _templates.ContainsKey(id) ? _templates[id] : null;
        }

        public Transfer Create(uint objectId, uint templateId, TransferSpawner spawner)
        {
            /*
            * A sequence of packets when a cart appears:
            * (the wagon itself consists of two parts and two benches for the characters)
            * "Salislead Peninsula ~ Liriot Hillside Loop Carriage"
            * SCUnitStatePacket(tlId0=GetNextId(), objId0=GetNextId(), templateId = 6, modelId = 654, attachPoint=255)
            * "The wagon boarding part"
            * SCUnitStatePacket(tlId2= tlId0, objId2=GetNextId(), templateId = 46, modelId = 653, attachPoint=30, objId=objId0)
            * SCDoodadCreatedPacket(templateId = 5890, attachPoint=2, objId=objId2, x1y1z1)
            * SCDoodadCreatedPacket(templateId = 5890, attachPoint=3, objId=objId2, x2y2z2)
            */

            if (!Exist(templateId)) { return null; }

            // create the cab of the carriage.
            var Carriage = GetTransferTemplate(templateId); // 6 - Salislead Peninsula ~ Liriot Hillside Loop Carriage
            var owner = new Transfer();
            owner.Name = Carriage.Name;
            owner.TlId = (ushort)TlIdManager.Instance.GetNextId();
            owner.ObjId = objectId == 0 ? ObjectIdManager.Instance.GetNextId() : objectId;
            owner.OwnerId = 255;
            owner.Spawner = spawner;
            owner.TemplateId = Carriage.Id;   // templateId
            owner.ModelId = Carriage.ModelId; // modelId
            owner.Template = Carriage;
            owner.BondingObjId = 0;    // objId
            owner.AttachPointId = 255; // point
            owner.Level = 1;
            owner.Hp = owner.MaxHp;
            owner.Mp = owner.MaxMp;
            owner.ModelParams = new UnitCustomModelParams();
            owner.Position = spawner.Position.Clone();
            owner.Position.RotationZ = spawner.Position.RotationZ;
            owner.Rot = new Quaternion(0f, 0f, spawner.RotationZ, 1f);

            owner.Faction = new SystemFaction();
            //owner.Faction = FactionManager.Instance.GetFaction(1);
            owner.Patrol = null;
            // add effect
            var buffId = 545u; //BUFF: Untouchable (Unable to attack this target)
            owner.Effects.AddEffect(new Effect(owner, owner, SkillCaster.GetByType(EffectOriginType.Skill), SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.Now));

            // create Carriage like a normal object.
            //owner.Spawn(); // in theory already spawned in SpawnManager
            _activeTransfers.Add(owner.ObjId, owner);

            if (Carriage.TransferBindings.Count <= 0) { return owner; }

            var boardingPart = GetTransferTemplate(Carriage.TransferBindings[0].TransferId); // 46 - The wagon boarding part
            var transfer = new Transfer();
            transfer.Name = boardingPart.Name;
            transfer.TlId = (ushort)TlIdManager.Instance.GetNextId();
            transfer.ObjId = ObjectIdManager.Instance.GetNextId();
            transfer.OwnerId = 255;
            transfer.Spawner = owner.Spawner;
            transfer.TemplateId = boardingPart.Id;   // templateId
            transfer.ModelId = boardingPart.ModelId; // modelId
            transfer.Template = boardingPart;
            transfer.Level = 1;
            transfer.BondingObjId = owner.ObjId;
            transfer.AttachPointId = owner.Template.TransferBindings[0].AttachPointId;
            transfer.Hp = transfer.MaxHp;
            transfer.Mp = transfer.MaxMp;
            transfer.ModelParams = new UnitCustomModelParams();
            transfer.Position = spawner.Position.Clone();
            transfer.Position.RotationZ = spawner.Position.RotationZ;

            transfer.Rot = new Quaternion(0f, 0f, spawner.RotationZ, 1f);
            transfer.Rot = new Quaternion(0f, 0f, 0, 1f);

            (transfer.Position.X, transfer.Position.Y) = MathUtil.AddDistanceToFront(-9.24417f, transfer.Position.X, transfer.Position.Y, transfer.Position.RotationZ);
            //var tempPoint = new Point(transfer.Position.WorldId, transfer.Position.ZoneId, -0.33f, -9.01f, 2.44f, 0, 0, 0);
            transfer.Position.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(transfer.Position.ZoneId, transfer.Position.X, transfer.Position.Y) : transfer.Position.Z;

            transfer.Faction = new SystemFaction();
            transfer.Patrol = null;
            // add effect
            buffId = 545u; //BUFF: Untouchable (Unable to attack this target)
            transfer.Effects.AddEffect(new Effect(transfer, transfer, SkillCaster.GetByType(EffectOriginType.Skill), SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.Now));
            owner.Bounded = transfer; // запомним параметры связанной части в родителе

            // create a boardingPart and indicate that we attach to the Carriage object 
            //transfer.Spawn();
            _activeTransfers.Add(transfer.ObjId, transfer);

            foreach (var doodadBinding in transfer.Template.TransferBindingDoodads)
            {
                var doodad = DoodadManager.Instance.Create(0, doodadBinding.DoodadId, transfer);
                doodad.ObjId = ObjectIdManager.Instance.GetNextId();
                doodad.TemplateId = doodadBinding.DoodadId;
                doodad.OwnerObjId = 0;
                doodad.ParentObjId = transfer.ObjId;
                doodad.Spawner = new DoodadSpawner();
                doodad.AttachPoint = (byte)doodadBinding.AttachPointId;
                switch (doodadBinding.AttachPointId)
                {
                    case 2:
                        doodad.Position = new Point(owner.Position.WorldId, owner.Position.ZoneId, 0.00537476f, 5.7852f, 1.36648f, 0, 0, 0);
                        break;
                    case 3:
                        doodad.Position = new Point(owner.Position.WorldId, owner.Position.ZoneId, 0.00537476f, 1.63614f, 1.36648f, 0, 0, -1);
                        break;
                }

                doodad.OwnerId = 0;
                doodad.PlantTime = DateTime.Now;
                doodad.OwnerType = DoodadOwnerType.System;
                doodad.DbHouseId = 0;
                doodad.Template = DoodadManager.Instance.GetTemplate(doodadBinding.DoodadId);
                doodad.Data = (byte)doodadBinding.AttachPointId;
                doodad.SetScale(1f);
                doodad.FuncGroupId = doodad.GetFuncGroupId();

                //doodad.Spawn();
                transfer.AttachedDoodads.Add(doodad);
            }

            return owner;
        }

        public void Load()
        {
            _templates = new Dictionary<uint, TransferTemplate>();
            _activeTransfers = new Dictionary<uint, Transfer>();

            _log.Info("Loading transfer templates...");

            #region SQLLite
            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM transfers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new TransferTemplate();

                            template.Id = reader.GetUInt32("id"); // OwnerId
                            template.Name = LocalizationManager.Instance.Get("transfers", "comment", reader.GetUInt32("id"));
                            template.ModelId = reader.GetUInt32("model_id");
                            template.WaitTime = reader.GetFloat("wait_time");
                            template.Cyclic = reader.GetBoolean("cyclic");
                            template.PathSmoothing = reader.GetFloat("path_smoothing");

                            _templates.Add(template.Id, template);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM transfer_bindings";
                    command.Prepare();

                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        //var step = 0u;
                        while (reader.Read())
                        {
                            var template = new TransferBindings
                            {
                                //Id = step++,
                                OwnerId = reader.GetUInt32("owner_id"),
                                OwnerType = reader.GetString("owner_type"),
                                AttachPointId = reader.GetByte("attach_point_id"),
                                TransferId = reader.GetUInt32("transfer_id")
                            };
                            if (_templates.ContainsKey(template.OwnerId))
                            {
                                _templates[template.OwnerId].TransferBindings.Add(template);
                            }
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM transfer_binding_doodads";
                    command.Prepare();

                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        //var step = 0u;
                        while (reader.Read())
                        {
                            var template = new TransferBindingDoodads
                            {
                                //Id = step++,
                                OwnerId = reader.GetUInt32("owner_id"),
                                OwnerType = reader.GetString("owner_type"),
                                AttachPointId = reader.GetInt32("attach_point_id"),
                                DoodadId = reader.GetUInt32("doodad_id"),
                            };
                            if (_templates.ContainsKey(template.OwnerId))
                            {
                                _templates[template.OwnerId].TransferBindingDoodads.Add(template);
                            }
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM transfer_paths";
                    command.Prepare();

                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        //var step = 0u;
                        while (reader.Read())
                        {
                            var template = new TransferPaths
                            {
                                //Id = step++,
                                OwnerId = reader.GetUInt32("owner_id"),
                                OwnerType = reader.GetString("owner_type"),
                                PathName = reader.GetString("path_name"),
                                WaitTimeStart = reader.GetDouble("wait_time_start"),
                                WaitTimeEnd = reader.GetDouble("wait_time_end")
                            };
                            if (_templates.ContainsKey(template.OwnerId))
                            {
                                _templates[template.OwnerId].TransferPaths.Add(template);
                            }
                        }
                    }
                }
            }
            #endregion
            #region TransferPath


            _log.Info("Loading transfer_path...");

            var worlds = WorldManager.Instance.GetWorlds();
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");

            //                              worldId           key   transfer_path
            _transferRoads = new Dictionary<byte, Dictionary<uint, List<TransferRoads>>>();
            foreach (var world in worlds)
            {
                var transferPaths = new Dictionary<uint, List<TransferRoads>>();
                for (uint zoneId = 129; zoneId < 346; zoneId++)
                {
                    var contents = FileManager.GetFileContents($"{FileManager.AppPath}Data/Worlds/{world.Name}/level_design/zone/{zoneId}/client/transfer_path.xml");
                    if (string.IsNullOrWhiteSpace(contents))
                    {
                        //_log.Warn($"{FileManager.AppPath}Data/Worlds/{world.Name}/level_design/zone/{zoneId}/client/transfer_path.xml doesn't exists or is empty.");
                        continue;
                    }

                    var transferPath = new List<TransferRoads>();
                    var xDoc = new XmlDocument();
                    xDoc.Load($"{FileManager.AppPath}Data/Worlds/{world.Name}/level_design/zone/{zoneId}/client/transfer_path.xml");
                    var xRoot = xDoc.DocumentElement;
                    foreach (XmlElement xnode in xRoot)
                    {
                        var transfers = new TransferRoads();
                        if (xnode.Attributes.Count > 0)
                        {
                            transfers.Name = xnode.Attributes.GetNamedItem("Name").Value;
                            transfers.Type = int.Parse(xnode.Attributes.GetNamedItem("Type").Value);
                            transfers.CellX = int.Parse(xnode.Attributes.GetNamedItem("cellX").Value);
                            transfers.CellY = int.Parse(xnode.Attributes.GetNamedItem("cellY").Value);
                        }
                        foreach (XmlNode childnode in xnode.ChildNodes)
                        {
                            foreach (XmlNode node in childnode.ChildNodes)
                            {
                                if (node.Attributes.Count > 0)
                                {
                                    var attributeValue = node.Attributes.GetNamedItem("Pos").Value;
                                    var splitVals = attributeValue.Split(',');
                                    if (splitVals.Length == 3)
                                    {
                                        var x = float.Parse(splitVals[0]);
                                        var y = float.Parse(splitVals[1]);
                                        var z = float.Parse(splitVals[2]);
                                        // конвертируем координаты из локальных в мировые, сразу при считывании из файла пути
                                        // но не учитываются dX и dY смещения по осям
                                        var xyz = new Vector3(x, y, z);
                                        var (xx, yy, zz) = ZoneManager.Instance.ConvertToWorldCoord(zoneId, xyz);
                                        //if (transfers.CellX > 0)
                                        //{
                                        //    xx += transfers.CellX * 16;
                                        //}
                                        //if (transfers.CellY > 0)
                                        //{
                                        //    yy += transfers.CellY * 16;
                                        //}
                                        var pos = new Point(xx, yy, zz)
                                        {
                                            WorldId = world.Id,
                                            ZoneId = zoneId
                                        };
                                        transfers.Pos.Add(pos);
                                    }
                                }
                            }
                        }
                        transferPath.Add(transfers);
                    }
                    transferPaths.Add(zoneId, transferPath);
                }
                _transferRoads.Add((byte)world.Id, transferPaths);
            }
            #endregion

            GetOwnerPaths();
        }
    }
}

