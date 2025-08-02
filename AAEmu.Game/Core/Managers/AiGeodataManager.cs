using System.Diagnostics;
using System.Numerics;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.CryEngine.Entities;
using AAEmu.Game.Models.CryEngine.Loaders;
using AAEmu.Game.Models.CryEngine.Mission;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using NLog;
using Org.BouncyCastle.Utilities.Bzip2;

#pragma warning disable IDE0079 // Remove unnecessary suppression

namespace AAEmu.Game.Core.Managers;

// GeoData AiNavigation
public class AiGeoDataManager(WorldTemplate worldTemplate)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<long, List<AiNavigation>> _aiNavigation;
    private Dictionary<long, string> _areasMission;
    private Dictionary<long, List<Vector3>> _forbiddenArea;
    private Dictionary<long, List<Vector3>> _aiPath;
    private Dictionary<long, List<Vector3>> _aiNavigationModifier;

    public List<LinkDescriptor> GetAvailablePoints(NodeDescriptor point)
    {
        return point.NetMission.LinkDescriptorList.Where(l => l.SourceNode == point.Id).ToList() ?? [];
    }

    #region A point in a polygon

    /// <summary>
    /// Checks if point is inside a forbidden zone area
    /// </summary>
    /// <param name="point"></param>
    /// <returns></returns>
    public bool CheckImpossibleWalk(Vector3 point)
    {
        if (_forbiddenArea.Count <= 1)
        {
            return false; // consider that we are inside the zone (i.e. limitation outside)
        }

        if (_forbiddenArea != null)
        {
            foreach (var fa in _forbiddenArea.Values)
            {
                if (IsInPolygon(point, fa))
                    return true;
            }
        }

        return false;
    }

    private static bool IsInPolygon(Vector3 point, List<Vector3> polygon)
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
    /// Get the center of the triangle (intersection of the medians)
    /// </summary>
    /// <param name="point1"></param>
    /// <param name="point2"></param>
    /// <param name="point3"></param>
    /// <returns></returns>
    public static Vector3 TriangleCenter(Vector3 point1, Vector3 point2, Vector3 point3)
    {
        var x = (point1.X + point2.X + point3.X) / 3;
        var y = (point1.Y + point2.Y + point3.Y) / 3;
        var z = (point1.Z + point2.Z + point3.Z) / 3;

        return new Vector3(x, y, z);
    }

    #endregion A point in a polygon

    #region Path smoothing

    // https://www.codeproject.com/Articles/18936/A-C-Implementation-of-Douglas-Peucker-Line-Appro
    public static List<Vector3> DouglasPeuckerReduction(List<Vector3> points, double tolerance)
    {
        if (points == null || points.Count < 3)
            return points;

        var firstPointIndex = 0;
        var lastPointIndex = points.Count - 1;
        var pointIndexesToKeep = new List<int>();

        //The first and the last point cannot be the same
        while (points[firstPointIndex].Equals(points[lastPointIndex]))
        {
            lastPointIndex--;
        }

        //Add the first and last index to the keepers
        pointIndexesToKeep.Add(firstPointIndex);
        pointIndexesToKeep.Add(lastPointIndex);

        DouglasPeuckerReduction(points, firstPointIndex, lastPointIndex, tolerance, ref pointIndexesToKeep);

        var returnPoints = new List<Vector3>();
        pointIndexesToKeep.Sort();
        foreach (var index in pointIndexesToKeep)
        {
            returnPoints.Add(points[index]);
        }

        return returnPoints;
    }

    /// <summary>
    /// Douglas-Peucker reduction.
    /// </summary>
    /// <param name="points">The points.</param>
    /// <param name="firstPointIndex">The first point.</param>
    /// <param name="lastPointIndex">The last point.</param>
    /// <param name="tolerance">The tolerance.</param>
    /// <param name="pointIndexesToKeep">The point index to keep.</param>
    private static void DouglasPeuckerReduction(List<Vector3> points, int firstPointIndex, int lastPointIndex, double tolerance, ref List<int> pointIndexesToKeep)
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
                pointIndexesToKeep.Add(indexFarthest);

                DouglasPeuckerReduction(points, firstPointIndex, indexFarthest, tolerance, ref pointIndexesToKeep);
                DouglasPeuckerReduction(points, indexFarthest, lastPointIndex, tolerance, ref pointIndexesToKeep);
            }
        }
    }

    /// <summary>
    /// The distance of a point from a line made from point1 and point2.
    /// </summary>
    /// <param name="point1">The point1.</param>
    /// <param name="point2">The point2.</param>
    /// <param name="targetPoint">The point.</param>
    /// <returns></returns>
    private static double PerpendicularDistance(Vector3 point1, Vector3 point2, Vector3 targetPoint)
    {
        //Area = |(1/2)(x1y2 + x2y3 + x3y1 - x2y1 - x3y2 - x1y3)|   *Area of triangle
        //Base = v((x1-x2)²+(x1-x2)²)                               *Base of Triangle*
        //Area = .5*Base*H                                          *Solve for height
        //Height = Area/.5/Base

        var area = Math.Abs(.5 * (point1.X * point2.Y + point2.X * targetPoint.Y + targetPoint.X * point1.Y - point2.X * point1.Y - targetPoint.X * point2.Y - point1.X * targetPoint.Y));
        var bottom = Math.Sqrt(Math.Pow(point1.X - point2.X, 2) + Math.Pow(point1.Y - point2.Y, 2));
        var height = area / bottom * 2;

        return height;
    }

    #endregion Path smoothing

    #region Finding the closest point

    public (uint, Vector3) FindСlosestToTheCurrent2(uint zoneKey, Vector3 pos)
    {
        var index = 0u;
        var point = new Vector3();

        foreach (var closest in _aiNavigation.Values.Select(lpf => lpf
                     .OrderBy(x => DistanceBetweenPoints(pos, x.Position))
                     .First()))
        {
            index = closest.StartPoint;
            point = closest.Position;
        }

        Logger.Warn($"# Found near position index: {index}...");
        return (index, point);
    }

    public NodeDescriptor FindСlosestToTheCurrent(uint zoneKey, Vector3 pos)
    {
        var posX = pos.X;
        var posY = pos.Y;

        NodeDescriptor closestPointFound = null;
        var minDist = 99999.0f;
        
        var (sourceCellX, sourceCellY) = pos.ToCellIndex();
        var cell = worldTemplate.GetCell(sourceCellX, sourceCellY);
        if (cell == null)
            return null;

        List<BaseBaiLoader> toCheckChunkList = [];
        if (cell.Template.ZoneBaiLoader.Count > 0)
        {
            // If the zoneKey is actually the pre-defined one, then just use that
            if (cell.Template.ZoneBaiLoader.TryGetValue(zoneKey, out var preDefined))
            {
                toCheckChunkList.Add(preDefined);
            }
            else
            {
                // Otherwise, check all of them
                foreach (var (_, bai) in cell.Template.ZoneBaiLoader)
                {
                    toCheckChunkList.Add(bai);
                }
            }
        }
        else
        {
            // If no zone defined (main_world), the use the 4x4 chunk grid of the cell
            foreach (var bai in cell.BaiLoader)
            {
                if (bai != null)
                    toCheckChunkList.Add(bai);
            }
        }

        // Check all eligible chunks
        foreach (var bLoader in toCheckChunkList)
        {
            if (bLoader == null)
                continue;
            foreach (var netMission in bLoader.NetMissionReaders)
            {
                foreach (var (_, nodeDescriptor) in netMission.NodeDescriptorList)
                {
                    var dx = posX - nodeDescriptor.Pos.X;
                    var dy = posY - nodeDescriptor.Pos.Y;

                    var distance = dx * dx + dy * dy;
                    if (distance < minDist)
                    {
                        closestPointFound = nodeDescriptor;
                        minDist = distance;
                    }
                }
            }
        }

        // Logger.Warn($"# Found near position index: {index}...");
        return closestPointFound;
    }

    /// <summary>
    /// Gets height using navmesh data
    /// </summary>
    /// <param name="pos"></param>
    /// <returns></returns>
    public float GetHeight(Vector3 pos)
    {
        float res;
        //var stopWatch = new Stopwatch();
        //stopWatch.Start();
        try
        {
            var posX = pos.X;
            var posY = pos.Y;

            var pointN = new Vector3();
            var pointFa = new Vector3();

            var minDistN = 99999.0f;
            var minDistFa = 99999.0f;


            foreach (var lpf in _aiNavigation.Values)
            {
                foreach (var pf in lpf)
                {
                    var dx = posX - pf.Position.X;
                    var dy = posY - pf.Position.Y;

                    var distance = dx * dx + dy * dy;
                    if (!(distance < minDistN)) { continue; }

                    pointN = pf.Position;
                    minDistN = distance;
                }
            }


            foreach (var lfa in _forbiddenArea.Values)
            {
                foreach (var pf in lfa)
                {
                    var dx = posX - pf.X;
                    var dy = posY - pf.Y;

                    var distance = dx * dx + dy * dy;
                    if (!(distance < minDistFa)) { continue; }

                    pointFa = pf;
                    minDistFa = distance;
                }
            }


            //Logger.Warn($"# Found near position aiNavigation, Z: {pointN.Z}...");
            res = minDistFa < minDistN ? pointFa.Z : pointN.Z;
        }
        catch
        {
            res = 0f;
        }
        //stopWatch.Stop();
        //Logger.Info($"GetHeight took {stopWatch.Elapsed}");

        return res;
    }

    private static float DistanceBetweenPoints(Vector3 point, Vector3 compareTo)
    {
        return (compareTo.X - point.X) * (compareTo.X - point.X) +
               (compareTo.Y - point.Y) * (compareTo.Y - point.Y);
    }

    private static Vector3 FindClosest(List<AiNavigation> searchIn, Vector3 compareTo)
    {
        return searchIn
            .Select(p => new { point = p.Position, distance = DistanceBetweenPoints(p.Position, compareTo) })
            .OrderBy(distances => distances.distance)
            .First().point;
    }

    private static Vector3 FindClosest(List<Vector3> searchIn, Vector3 compareTo)
    {
        return searchIn
            .Select(p => new { point = p, distance = DistanceBetweenPoints(p, compareTo) })
            .OrderBy(distances => distances.distance)
            .First().point;
    }

    /// <summary>
    /// Find the nearest point
    /// </summary>
    /// <param name="searchIn"></param>
    /// <param name="compareTo"></param>
    /// <returns>returns the index of the found point</returns>
    public static uint FindClosestIndexPoint(List<Vector3> searchIn, Vector3 compareTo)
    {
        var minDistance = 0f;
        var pointN = 0u;

        for (var i = 0; i < searchIn.Count; i++)
        {
            var distance = DistanceBetweenPoints(searchIn[i], compareTo);
            if (distance > minDistance)
                continue;

            pointN = (uint)i;
            minDistance = distance;
        }

        return pointN;
    }

    public float GetHeight2(uint zoneKey, Vector3 pos)
    {
        float res;
        var position = new Vector3(pos.X, pos.Y, pos.Z);
        var stopWatch = new Stopwatch();
        stopWatch.Start();
        try
        {
            var pointsList = new List<Vector3>();
            pointsList.AddRange(_aiNavigation.Values.Select(nav => FindClosest(nav, position)));
            pointsList.AddRange(_forbiddenArea.Values.Select(fa => FindClosest(fa, position)));
            var point = pointsList.OrderBy(p => DistanceBetweenPoints(pos, p)).First();
            //Logger.Warn($"# Found near position aiNavigation, Z: {pointN.Z}...");
            res = point.Z;
        }
        catch
        {
            res = 0f;
        }

        stopWatch.Stop();
        Logger.Info($"GetHeight2 took {stopWatch.Elapsed}");
        return res;
    }

    #endregion Finding the closest point

    #region SQLite

    public void Load()
    {
        Logger.Info($"Loading AI GeoData for {worldTemplate} ...");

        _aiNavigation = [];
        _areasMission = [];
        _forbiddenArea = [];
        _aiPath = [];
        _aiNavigationModifier = [];

        var worldPath = Path.Combine("Data", "AiGeoData", worldTemplate.Name);
        var worldPathToFile = Path.Combine(worldPath, "server_ai_geo_data.sqlite3");
        if (!File.Exists(worldPathToFile))
        {
            Logger.Info($"World {worldTemplate.Name} is missing {Path.GetFileName(worldPathToFile)}");
        }
        else
        {
            using var connection = SQLite.CreateConnection(worldPath, "server_ai_geo_data.sqlite3");
            Logger.Info("Loading ai_navigation...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM ai_navigation";
                command.Prepare();
                using (var sqliteDataReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                {
                    while (reader.Read())
                    {
                        var template = new AiNavigation
                        {
                            Id = reader.GetUInt32("id"),
                            ZoneKey = reader.GetUInt32("zone_key"),
                            StartPoint = reader.GetUInt32("start_point"),
                            EndPoint = reader.GetUInt32("end_point"),
                            Position = new Vector3()
                            {
                                X = reader.GetFloat("x"),
                                Y = reader.GetFloat("y"),
                                Z = reader.GetFloat("z")
                            }
                        };

                        // convert coordinates from local to world, immediately when reading the path from the file
                        var xyz = new Vector3(template.Position.X, template.Position.Y, template.Position.Z);
                        var vec = ZoneManager.ConvertToWorldCoordinates(template.ZoneKey, xyz);
                        template.Position = vec;

                        if (_aiNavigation.TryGetValue(template.StartPoint, out var value))
                        {
                            value.Add(template);
                        }
                        else
                        {
                            _aiNavigation.Add(template.StartPoint, [template]);
                        }
                    }
                }
            }

            Logger.Info($"Loading areas_mission for {worldTemplate} ...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM areas_mission";
                command.Prepare();
                using (var sqliteDataReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                {
                    while (reader.Read())
                    {
                        var template = new AreasMission
                        {
                            Id = reader.GetUInt32("id"),
                            ZoneKey = reader.GetUInt32("zone_key"),
                            Name = reader.GetString("name"),
                            Type = reader.GetString("type"),
                            PointCount = reader.GetUInt32("point_count")
                        };

                        _areasMission.Add(template.Id, template.Type);
                    }
                }
            }

            Logger.Info($"Loading areas_mission_points for {worldTemplate} ...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM areas_mission_points";
                command.Prepare();
                using (var sqliteDataReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                {
                    while (reader.Read())
                    {
                        var template = new AreasMissionPoints
                        {
                            Id = reader.GetUInt32("id"),
                            ZoneKey = reader.GetUInt32("zone_key"),
                            Position = new Vector3()
                            {
                                X = reader.GetFloat("x"),
                                Y = reader.GetFloat("y"),
                                Z = reader.GetFloat("z")
                            }
                        };

                        // convert coordinates from local to world, immediately when reading the path from the file
                        var xyz = new Vector3(template.Position.X, template.Position.Y, template.Position.Z);
                        var vec = ZoneManager.ConvertToWorldCoordinates(template.ZoneKey, xyz);
                        template.Position = vec;

                        var type = _areasMission[template.Id];
                        switch (type)
                        {
                            case "ForbiddenArea":
                                if (_forbiddenArea.TryGetValue(template.Id, out var value))
                                {
                                    value.Add(template.Position);
                                }
                                else
                                {
                                    _forbiddenArea.Add(template.Id, [template.Position]);
                                }

                                break;
                            case "AINavigationModifier":
                                if (_aiNavigationModifier.TryGetValue(template.Id, out var value1))
                                {
                                    value1.Add(template.Position);
                                }
                                else
                                {
                                    _aiNavigationModifier.Add(template.Id, [template.Position]);
                                }

                                break;
                            case "AIPath":
                                if (_aiPath.TryGetValue(template.Id, out var value2))
                                {
                                    value2.Add(template.Position);
                                }
                                else
                                {
                                    _aiPath.Add(template.Id, [template.Position]);
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
