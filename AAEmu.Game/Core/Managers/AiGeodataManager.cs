using System;
using System.Collections.Generic;
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
        private static Logger s_log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, List<AiNavigation>> _aiNavigation;
        private Dictionary<uint, string> _areasMission;
        private Dictionary<uint, List<Point>> _forbiddenArea;
        private Dictionary<uint, List<Point>> _aiPath;
        private Dictionary<uint, List<Point>> _aiNavigationModifier;

        public List<AiNavigation> GetAvailablePoints(uint point)
        {
            _aiNavigation.TryGetValue(point, out var ret);
            return ret;
        }

        #region A point in a polygon

        public bool CheckImpossibleWalk(Point point)
        {
            var res = new List<bool>();
            foreach (var forbiddenArea in _forbiddenArea.Values)
            {
                res.Add(IsInPolygon(point, forbiddenArea));
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

        public (uint, Point) FindСlosestToTheCurrent(Vector3 pos)
        {
            var index = 0u;
            var point = new Point();
            var minDist = 99999.0f;

            foreach (var lpf in _aiNavigation.Values)
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

            s_log.Warn($"# Found near position index: {index}...");
            return (index, point);
        }

        #endregion Finding the closest point

        #region SQLite

        public void Load()
        {
            s_log.Info("Loading AI GeoData...");

            _aiNavigation = new Dictionary<uint, List<AiNavigation>>();
            _areasMission = new Dictionary<uint, string>();
            _forbiddenArea = new Dictionary<uint, List<Point>>();
            _aiPath = new Dictionary<uint, List<Point>>();
            _aiNavigationModifier = new Dictionary<uint, List<Point>>();

            // We load only Arche_mall
            using (var connection = SQLite.CreateConnection("Data\\AiGeoData\\260", "server_ai_geo_data.sqlite3"))
            {
                s_log.Info("Loading ai_navigation...");
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

                            if (_aiNavigation.ContainsKey(template.StartPoint))
                            {
                                _aiNavigation[template.StartPoint].Add(template);
                            }
                            else
                            {
                                _aiNavigation.Add(template.StartPoint, new List<AiNavigation> { template });
                            }
                        }
                    }
                }

                s_log.Info("Loading areas_mission...");
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM areas_mission";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        while (reader.Read())
                        {
                            var areasMission = new AreasMission();
                            areasMission.Id = reader.GetUInt32("id");
                            areasMission.ZoneKey = reader.GetUInt32("zone_key");
                            areasMission.Name = reader.GetString("name");
                            areasMission.Type = reader.GetString("type");
                            areasMission.PointCount = reader.GetUInt32("point_count");

                            _areasMission.Add(areasMission.Id, areasMission.Type);
                        }
                    }
                }

                s_log.Info("Loading areas_mission_points...");
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM areas_mission_points";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        var dictAmp = new Dictionary<uint, List<Point>>();
                        while (reader.Read())
                        {
                            var areasMissionPoints = new AreasMissionPoints();
                            areasMissionPoints.Id = reader.GetUInt32("id");
                            areasMissionPoints.ZoneKey = reader.GetUInt32("zone_key");
                            areasMissionPoints.Position = new Point();
                            areasMissionPoints.Position.X = reader.GetFloat("x");
                            areasMissionPoints.Position.Y = reader.GetFloat("y");
                            areasMissionPoints.Position.Z = reader.GetFloat("z");

                            // convert coordinates from local to world, immediately when reading the path from the file
                            var xyz = new Vector3(areasMissionPoints.Position.X, areasMissionPoints.Position.Y, areasMissionPoints.Position.Z);
                            var vec = ZoneManager.Instance.ConvertToWorldCoordinates(areasMissionPoints.ZoneKey, xyz);
                            areasMissionPoints.Position.X = vec.X;
                            areasMissionPoints.Position.Y = vec.Y;
                            areasMissionPoints.Position.Z = vec.Z;

                            var type = _areasMission[areasMissionPoints.Id];
                            switch (type)
                            {
                                case "ForbiddenArea":
                                    if (_forbiddenArea.ContainsKey(areasMissionPoints.Id))
                                    {
                                        _forbiddenArea[areasMissionPoints.Id].Add(areasMissionPoints.Position);
                                    }
                                    else
                                    {
                                        _forbiddenArea.Add(areasMissionPoints.Id, new List<Point> { areasMissionPoints.Position });
                                    }
                                    break;
                                case "AINavigationModifier":
                                    if (_aiNavigationModifier.ContainsKey(areasMissionPoints.Id))
                                    {
                                        _aiNavigationModifier[areasMissionPoints.Id].Add(areasMissionPoints.Position);
                                    }
                                    else
                                    {
                                        _aiNavigationModifier.Add(areasMissionPoints.Id, new List<Point> { areasMissionPoints.Position });
                                    }
                                    break;
                                case "AIPath":
                                    if (_aiPath.ContainsKey(areasMissionPoints.Id))
                                    {
                                        _aiPath[areasMissionPoints.Id].Add(areasMissionPoints.Position);
                                    }
                                    else
                                    {
                                        _aiPath.Add(areasMissionPoints.Id, new List<Point> { areasMissionPoints.Position });
                                    }
                                    break;
                            }
                        }
                    }
                }
            }
        }

        #endregion
    }
}
