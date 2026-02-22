using System.Numerics;

using AAEmu.Game.Models.CryEngine.Entities;
using AAEmu.Game.Models.CryEngine.Loaders;
using AAEmu.Game.Models.CryEngine.Mission;
using AAEmu.Game.Models.Game.AI.AStar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

using NLog;

#pragma warning disable IDE0079 // Remove unnecessary suppression

namespace AAEmu.Game.Core.Managers;

// GeoData AiNavigation
public class AiGeoDataManager(WorldTemplate worldTemplate)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

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
        var bai = worldTemplate.GetBaiByPos(point);
        if (bai != null)
        {
            foreach (var areaMission in bai.AreasMissionReaders)
            {
                foreach (var forbiddenArea in areaMission.ForbiddenAreasList)
                {
                    if (IsInPolygon(point, forbiddenArea.Points))
                        return true;
                }
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
                    var distance = (nodeDescriptor.Pos - pos).Length();
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
        try
        {
            var closestPoint = Vector3.Zero;
            var closestDistance2D = float.MaxValue;

            // Only use NetMission walkable nodes for terrain height.
            // VertexMission contains obstacle positions (rocks, trees, fences)
            // whose Z represents the obstacle top, NOT walkable ground.
            var bai = worldTemplate.GetBaiByPos(pos);
            if (bai != null)
            {
                foreach (var netMission in bai.NetMissionReaders)
                {
                    foreach (var (_, nodeDescriptor) in netMission.NodeDescriptorList)
                    {
                        var dx = nodeDescriptor.Pos.X - pos.X;
                        var dy = nodeDescriptor.Pos.Y - pos.Y;
                        var dist2D = dx * dx + dy * dy;
                        if (dist2D < closestDistance2D)
                        {
                            closestDistance2D = dist2D;
                            closestPoint = nodeDescriptor.Pos;
                            if (closestDistance2D < 0.01f)
                                return closestPoint.Z;
                        }
                    }
                }
            }

            // If nearest walkable node is too far (>5 units in 2D), its height
            // is unreliable for this position — fall back to heightmap
            if (closestDistance2D > 5f * 5f || closestDistance2D >= float.MaxValue)
            {
                return worldTemplate.GetRawHeightMapHeight((int)MathF.Round(pos.X), (int)MathF.Round(pos.Y));
            }

            return closestPoint.Z;
        }
        catch
        {
            return 0f;
        }
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

    #endregion Finding the closest point

    public void Load()
    {
        // Nothing to load here anymore, everything
    }

    public Queue<Vector3> ReducePath(List<Vector3> foundPath, int maxNodeSkipCount)
    {
        var res = new Queue<Vector3>();
        // Check for all node
        for (var startNodeIndex = 0; startNodeIndex < foundPath.Count; startNodeIndex++)
        {
            var startNode = foundPath[startNodeIndex];
            res.Enqueue(startNode);
            // Check nodes further in the path, starting at the furthest node defined by max skip (and getting closer with each loop)
            for (var endNodeIndex = startNodeIndex + maxNodeSkipCount; endNodeIndex > startNodeIndex; endNodeIndex--)
            {
                // Check if still in total range
                if (endNodeIndex >= foundPath.Count)
                    continue;
                var endNode = foundPath[endNodeIndex];
                // Skip this node if the height offset is too much
                var delta = endNode - startNode;
                var angleRate = delta.Length() > 0 ? delta.Z / delta.Length() : 0f;
                if (angleRate >= 0.2f || angleRate <= -0.5f)
                    continue;
                // Check if there's a direct line between the two nodes that is allowed
                if (LinePassesThroughForbiddenArea(startNode, endNode) == false)
                {
                    // If clear, directly put this point as next, and move the check index
                    res.Enqueue(endNode);
                    startNodeIndex = endNodeIndex;
                    break;
                }
            }
        }

        return res;
    }

    /// <summary>
    /// Checks if a line passes through at least one of the edges of a AiShape
    /// </summary>
    /// <param name="startPos"></param>
    /// <param name="endPos"></param>
    /// <param name="shape"></param>
    /// <param name="closedLoop">Is the shape a closed loop</param>
    /// <param name="maxHeightOffset">Maximum height difference required for the intersection to count as valid</param>
    /// <returns></returns>
    private bool LinePassesThroughAiShape(Vector3 startPos, Vector3 endPos, AiShape shape, bool closedLoop, float maxHeightOffset)
    {
        for (var index = 0; index < shape.Points.Count + (closedLoop ? 0 : -1); index++)
        {
            var lineStart = shape.Points[index];
            var lineEnd = index < shape.Points.Count-1 ? shape.Points[index + 1] : shape.Points[0];
            var intersectionPoint = FindLineIntersection(startPos, endPos, lineStart, lineEnd); 
            if (intersectionPoint != Vector3.Zero)
            {
                if (maxHeightOffset == 0f || MathF.Abs(intersectionPoint.Z - startPos.Z) <= maxHeightOffset || MathF.Abs(intersectionPoint.Z - endPos.Z) <= maxHeightOffset)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Check if a given line passes any of the defined ForbiddenAreas nearby (in 2D space)
    /// </summary>
    /// <param name="startNode"></param>
    /// <param name="endNode"></param>
    /// <returns></returns>
    public bool LinePassesThroughForbiddenArea(Vector3 startNode, Vector3 endNode)
    {
        // It should be enough to grab the starting node's bai data. Forbidden zones are defined if even part of the zone falls within the area
        var sourceBai = worldTemplate.GetBaiByPos(startNode);
        if (sourceBai == null)
            return false; // No navmesh data — assume clear path
        foreach (var areaMission in sourceBai.AreasMissionReaders)
        {
            // Loop forbidden areas shape
            foreach (var aiShape in areaMission.ForbiddenAreasList)
            {
                if (LinePassesThroughAiShape(startNode, endNode, aiShape, true, 8f))
                    return true;
            }

            foreach (var aiShape in areaMission.ForbiddenBoundariesList)
            {
                if (LinePassesThroughAiShape(startNode, endNode, aiShape, true, 8f))
                    return true;
            }

            foreach (var aiShape in areaMission.DesignerForbiddenAreasList)
            {
                if (LinePassesThroughAiShape(startNode, endNode, aiShape, true, 8f))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if two lines intersect with given starting and ending point in 2D space (Z is ignored here)
    /// </summary>
    /// <param name="start1"></param>
    /// <param name="end1"></param>
    /// <param name="start2"></param>
    /// <param name="end2"></param>
    /// <returns>Returns the intersection point of line 1 in 2D space, or Zero if none was found</returns>
    /// <remarks>Based on the answer of https://stackoverflow.com/questions/1119451/how-to-tell-if-a-line-intersects-a-polygon-in-c#1120126</remarks>
    private static Vector3 FindLineIntersection(Vector3 start1, Vector3 end1, Vector3 start2, Vector3 end2)
    {
        var denominator = (end1.X - start1.X) * (end2.Y - start2.Y) - (end1.Y - start1.Y) * (end2.X - start2.X);

        // AB & CD are parallel 
        if (denominator == 0)
            return Vector3.Zero;

        var numerator1 = (start1.Y - start2.Y) * (end2.X - start2.X) - (start1.X - start2.X) * (end2.Y - start2.Y);
        var r = numerator1 / denominator;
        var numerator2 = (start1.Y - start2.Y) * (end1.X - start1.X) - (start1.X - start2.X) * (end1.Y - start1.Y);
        var s = numerator2 / denominator;

        if (r < 0 || r > 1 || s < 0 || s > 1)
            return Vector3.Zero;

        // Find intersection point
        return new Vector3(start1.X + r * (end1.X - start1.X), start1.Y + r * (end1.Y - start1.Y), start1.Z + r * (end1.Z - start1.Z));
    }

    /// <summary>
    /// Changes Z positions if they are above the floor
    /// </summary>
    /// <param name="pointsList"></param>
    /// <returns></returns>
    public List<Vector3> StickToFloor(List<Vector3> pointsList)
    {
        var res = new List<Vector3>();
        foreach (var point in pointsList)
        {
            var floor = worldTemplate.GetHeight(point.X, point.Y);
            if (floor < point.Z)
                res.Add(point with { Z = floor });
            else
                res.Add(point);
        }
        return res;
    }

    public Vector3 StickToFloor(Vector3 point)
    {
        var floor = worldTemplate.GetHeight(point.X, point.Y);
        if (floor < point.Z)
            return point with { Z = floor };
        return point;
    }
}
