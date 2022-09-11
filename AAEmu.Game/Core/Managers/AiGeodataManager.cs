using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using NLog;

using Point = AAEmu.Game.Models.Game.AI.AStar.Point;

namespace AAEmu.Game.Core.Managers
{
    // GeoData AiNavigation
    public class AiGeoDataManager : Singleton<AiGeoDataManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<byte, Dictionary<uint, List<AiNavigation>>> _aiNavigation;
        private Dictionary<byte, Dictionary<uint, string>> _areasMission;
        private Dictionary<byte, Dictionary<uint, List<Point>>> _forbiddenArea;
        private Dictionary<byte, Dictionary<uint, List<Point>>> _aiPath;
        private Dictionary<byte, Dictionary<uint, List<Point>>> _aiNavigationModifier;

        public List<AiNavigation> GetAvailablePoints(uint zoneKey, uint point)
        {
            var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);

            var ret = new List<AiNavigation>();
            _aiNavigation.TryGetValue((byte)worldId, out var aiNavigation);
            aiNavigation?.TryGetValue(point, out ret);

            return ret;
        }

        #region A point in a polygon

        public bool CheckImpossibleWalk(uint zoneKey, Point point)
        {
            var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);

            var res = new List<bool>();
            _forbiddenArea.TryGetValue((byte)worldId, out var forbiddenArea);

            if (forbiddenArea != null && forbiddenArea.Count <= 1)
            {
                return false; // consider that we are inside the zone (i.e. limitation outside)
            }

            if (forbiddenArea != null)
            {
                foreach (var fa in forbiddenArea.Values)
                {
                    res.Add(IsInPolygon(point, fa));
                }
            }

            return res.Any(b => b);
        }

        private static bool IsInPolygon(Point point, List<Point> polygon)
        {
            var result = false;
            var a = polygon.Last();
            foreach (var b in polygon)
            {
                if (b.X.Equals(point.X) && b.Y.Equals(point.Y))
                    return true;

                if (b.Y.Equals(a.Y) && point.Y.Equals(a.Y))
                {
                    if (a.X <= point.X && point.X <= b.X)
                        return true;

                    if (b.X <= point.X && point.X <= a.X)
                        return true;
                }

                if (b.Y < point.Y && a.Y >= point.Y || a.Y < point.Y && b.Y >= point.Y)
                {
                    if (b.X + (point.Y - b.Y) / (a.Y - b.Y) * (a.X - b.X) <= point.X)
                        result = !result;
                }
                a = b;
            }
            return result;
        }

        /// <summary>
        /// получить центр треугольника (пересеение медиан)
        /// </summary>
        /// <param name="point1"></param>
        /// <param name="point2"></param>
        /// <param name="point3"></param>
        /// <returns></returns>
        public static Vector3 TriangleCenter(Point point1, Point point2, Point point3)
        {
            var x = (point1.X + point2.X + point3.X) / 3;
            var y = (point1.Y + point2.Y + point3.Y) / 3;
            var z = (point1.Z + point2.Z + point3.Z) / 3;

            return new Vector3(x, y, z);
        }

        #endregion A point in a polygon

        #region Path smoothing

        public static List<Point> DouglasPeuckerReduction(List<Point> points, double tolerance)
        {
            if (points == null || points.Count < 3)
                return points;

            var firstPointIndex = 0;
            var lastPointIndex = points.Count - 1;
            var pointIndexsToKeep = new List<int>();

            //The first and the last point cannot be the same
            while (points[firstPointIndex].Equals(points[lastPointIndex]))
            {
                lastPointIndex--;
            }

            //Add the first and last index to the keepers
            pointIndexsToKeep.Add(firstPointIndex);
            pointIndexsToKeep.Add(lastPointIndex);

            DouglasPeuckerReduction(points, firstPointIndex, lastPointIndex, tolerance, ref pointIndexsToKeep);

            var returnPoints = new List<Point>();
            pointIndexsToKeep.Sort();
            foreach (var index in pointIndexsToKeep)
            {
                returnPoints.Add(points[index]);
            }

            return returnPoints;
        }

        /// <summary>
        /// Douglases the peucker reduction.
        /// </summary>
        /// <param name="points">The points.</param>
        /// <param name="firstPointIndex">The first point.</param>
        /// <param name="lastPointIndex">The last point.</param>
        /// <param name="tolerance">The tolerance.</param>
        /// <param name="pointIndexsToKeep">The point index to keep.</param>
        private static void DouglasPeuckerReduction(List<Point> points, int firstPointIndex, int lastPointIndex, double tolerance, ref List<int> pointIndexsToKeep)
        {
            double maxDistance = 0;
            var indexFarthest = 0;

            if (lastPointIndex - firstPointIndex > 1) // ADDITION: need to have more than two points in the set we are looking through
            {
                for (var index = firstPointIndex; index < lastPointIndex; index++)
                {
                    var distance = PerpendicularDistance(points[firstPointIndex], points[lastPointIndex], points[index]);
                    if (distance > maxDistance)
                    {
                        maxDistance = distance;
                        indexFarthest = index;
                    }
                }

                if (maxDistance > tolerance && indexFarthest != firstPointIndex) // CHANGE: condition was wrong.
                {
                    //Add the largest point that exceeds the tolerance
                    pointIndexsToKeep.Add(indexFarthest);

                    DouglasPeuckerReduction(points, firstPointIndex, indexFarthest, tolerance, ref pointIndexsToKeep);
                    DouglasPeuckerReduction(points, indexFarthest, lastPointIndex, tolerance, ref pointIndexsToKeep);
                }
            }
        }

        /// <summary>
        /// The distance of a point from a line made from point1 and point2.
        /// </summary>
        /// <param name="point1">The point1.</param>
        /// <param name="point2">The point2.</param>
        /// <param name="point">The point.</param>
        /// <returns></returns>
        private static double PerpendicularDistance(Point point1, Point point2, Point point)
        {
            //Area = |(1/2)(x1y2 + x2y3 + x3y1 - x2y1 - x3y2 - x1y3)|   *Area of triangle
            //Base = v((x1-x2)²+(x1-x2)²)                               *Base of Triangle*
            //Area = .5*Base*H                                          *Solve for height
            //Height = Area/.5/Base

            var area = Math.Abs(.5 * (point1.X * point2.Y + point2.X * point.Y + point.X * point1.Y - point2.X * point1.Y - point.X * point2.Y - point1.X * point.Y));
            var bottom = Math.Sqrt(Math.Pow(point1.X - point2.X, 2) + Math.Pow(point1.Y - point2.Y, 2));
            var height = area / bottom * 2;

            return height;
        }

        #endregion Path smoothing

        #region Finding the closest point

        public (uint, Point) FindСlosestToTheCurrent(uint zoneKey, Vector3 pos)
        {
            var index = 0u;
            var point = new Point();
            var minDist = 99999.0f;

            var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);
            
            _aiNavigation.TryGetValue((byte)worldId, out var aiNavigation);
            if (aiNavigation != null)
            {
                foreach (var lpf in aiNavigation.Values)
                {
                    foreach (var pf in lpf)
                    {
                        var vPosition = new Vector3(pf.Position.X, pf.Position.Y, pf.Position.Z);
                        var distance = MathUtil.GetDistance(pos, vPosition);
                        if (distance < minDist)
                        {
                            index = pf.StartPoint;
                            point = pf.Position;
                            minDist = distance;
                        }
                    }
                }
            }

            _log.Warn($"# Found near position index: {index}...");
            return (index, point);
        }

        public float GetHeight(uint zoneKey, Vector3 pos)
        {
            try
            {
                var worldId = WorldManager.Instance.GetWorldIdByZone(zoneKey);

                var point = new Point();
                var minDist = 99999.0f;

                _aiNavigation.TryGetValue((byte)worldId, out var aiNavigation);
                if (aiNavigation != null)
                {
                    foreach (var lpf in aiNavigation.Values)
                    {
                        foreach (var pf in lpf)
                        {
                            var vPosition = new Vector3(pf.Position.X, pf.Position.Y, pf.Position.Z);
                            var distance = MathUtil.GetDistance(pos, vPosition);
                            if (distance < minDist)
                            {
                                point = pf.Position;
                                minDist = distance;
                            }
                        }
                    }
                }

                _log.Warn($"# Found near position, Z: {point.Z}...");
                return point.Z;
            }
            catch
            {
                return 0f;
            }
        }

        #endregion Finding the closest point

        #region SQLite

        public void Load()
        {
            _log.Info("Loading AI GeoData...");

            _aiNavigation = new Dictionary<byte, Dictionary<uint, List<AiNavigation>>>();
            _areasMission = new Dictionary<byte, Dictionary<uint, string>>();
            _forbiddenArea = new Dictionary<byte, Dictionary<uint, List<Point>>>();
            _aiPath = new Dictionary<byte, Dictionary<uint, List<Point>>>();
            _aiNavigationModifier = new Dictionary<byte, Dictionary<uint, List<Point>>>();

            var worlds = WorldManager.Instance.GetWorlds();
            foreach (var world in worlds)
            {
                _aiNavigation = new Dictionary<byte, Dictionary<uint, List<AiNavigation>>>();
                _areasMission = new Dictionary<byte, Dictionary<uint, string>>();
                _forbiddenArea = new Dictionary<byte, Dictionary<uint, List<Point>>>();
                _aiPath = new Dictionary<byte, Dictionary<uint, List<Point>>>();
                _aiNavigationModifier = new Dictionary<byte, Dictionary<uint, List<Point>>>();
            }

            foreach (var world in worlds)
            {
                // TODO добавить в worlds => Geodata
                var aiNavigation = new Dictionary<uint, List<AiNavigation>>();
                var areasMission = new Dictionary<uint, string>();
                var forbiddenArea = new Dictionary<uint, List<Point>>();
                var aiPath = new Dictionary<uint, List<Point>>();
                var aiNavigationModifier = new Dictionary<uint, List<Point>>();

                var worldPath = Path.Combine("Data", "AiGeoData", world.Name);
                var worldPathToFile = Path.Combine(worldPath, "server_ai_geo_data.sqlite3");
                if (!File.Exists(worldPathToFile))
                {
                    _log.Info($"World {world.Name} is missing {Path.GetFileName(worldPathToFile)}");
                }
                else
                {
                    using (var connection = SQLite.CreateConnection(worldPath, "server_ai_geo_data.sqlite3"))
                    {
                        _log.Info("Loading ai_navigation...");
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "SELECT * FROM ai_navigation";
                            command.Prepare();
                            using (var sqliteDataReader = command.ExecuteReader())
                            using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                            {
                                while (reader.Read())
                                {
                                    var template = new AiNavigation();
                                    template.Id = reader.GetUInt32("id");
                                    template.ZoneKey = reader.GetUInt32("zone_key");
                                    template.StartPoint = reader.GetUInt32("start_point");
                                    template.EndPoint = reader.GetUInt32("end_point");
                                    template.Position = new Point();
                                    template.Position.X = reader.GetFloat("x");
                                    template.Position.Y = reader.GetFloat("y");
                                    template.Position.Z = reader.GetFloat("z");

                                    // convert coordinates from local to world, immediately when reading the path from the file
                                    var xyz = new Vector3(template.Position.X, template.Position.Y, template.Position.Z);
                                    var vec = ZoneManager.Instance.ConvertToWorldCoordinates(template.ZoneKey, xyz);
                                    template.Position.X = vec.X;
                                    template.Position.Y = vec.Y;
                                    template.Position.Z = vec.Z;

                                    if (aiNavigation.ContainsKey(template.StartPoint))
                                    {
                                        aiNavigation[template.StartPoint].Add(template);
                                    }
                                    else
                                    {
                                        aiNavigation.Add(template.StartPoint, new List<AiNavigation> { template });
                                    }
                                }
                            }
                        }

                        _log.Info("Loading areas_mission...");
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "SELECT * FROM areas_mission";
                            command.Prepare();
                            using (var sqliteDataReader = command.ExecuteReader())
                            using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                            {
                                while (reader.Read())
                                {
                                    var template = new AreasMission();
                                    template.Id = reader.GetUInt32("id");
                                    template.ZoneKey = reader.GetUInt32("zone_key");
                                    template.Name = reader.GetString("name");
                                    template.Type = reader.GetString("type");
                                    template.PointCount = reader.GetUInt32("point_count");

                                    areasMission.Add(template.Id, template.Type);
                                }
                            }
                        }

                        _log.Info("Loading areas_mission_points...");
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "SELECT * FROM areas_mission_points";
                            command.Prepare();
                            using (var sqliteDataReader = command.ExecuteReader())
                            using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                            {
                                while (reader.Read())
                                {
                                    var template = new AreasMissionPoints();
                                    template.Id = reader.GetUInt32("id");
                                    template.ZoneKey = reader.GetUInt32("zone_key");
                                    template.Position = new Point();
                                    template.Position.X = reader.GetFloat("x");
                                    template.Position.Y = reader.GetFloat("y");
                                    template.Position.Z = reader.GetFloat("z");

                                    // convert coordinates from local to world, immediately when reading the path from the file
                                    var xyz = new Vector3(template.Position.X, template.Position.Y, template.Position.Z);
                                    var vec = ZoneManager.Instance.ConvertToWorldCoordinates(template.ZoneKey, xyz);
                                    template.Position.X = vec.X;
                                    template.Position.Y = vec.Y;
                                    template.Position.Z = vec.Z;

                                    var type = areasMission[template.Id];
                                    switch (type)
                                    {
                                        case "ForbiddenArea":
                                            if (forbiddenArea.ContainsKey(template.Id))
                                            {
                                                forbiddenArea[template.Id].Add(template.Position);
                                            }
                                            else
                                            {
                                                forbiddenArea.Add(template.Id, new List<Point> { template.Position });
                                            }
                                            break;
                                        case "AINavigationModifier":
                                            if (aiNavigationModifier.ContainsKey(template.Id))
                                            {
                                                aiNavigationModifier[template.Id].Add(template.Position);
                                            }
                                            else
                                            {
                                                aiNavigationModifier.Add(template.Id, new List<Point> { template.Position });
                                            }
                                            break;
                                        case "AIPath":
                                            if (aiPath.ContainsKey(template.Id))
                                            {
                                                aiPath[template.Id].Add(template.Position);
                                            }
                                            else
                                            {
                                                aiPath.Add(template.Id, new List<Point> { template.Position });
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                    }
                }
                _aiNavigation[(byte)world.Id] = aiNavigation;
                _areasMission[(byte)world.Id] = areasMission;
                _forbiddenArea[(byte)world.Id] = forbiddenArea;
                _aiNavigationModifier[(byte)world.Id] = aiNavigationModifier;
                _aiPath[(byte)world.Id] = aiPath;
            }
        }

        #endregion
    }
}
