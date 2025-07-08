// https://lsreg.ru/realizaciya-algoritma-poiska-a-na-c/

using System.Collections.ObjectModel;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.AStar;

/// <summary>
/// Reusable A* pathfinder.
/// </summary>
public class PathNode
{
    /// <summary>
    /// Current zone.Id 
    /// </summary>
    public uint ZoneKey { get; set; }

    /// <summary>
    /// The number of the current point on the map.
    /// </summary>
    public uint Current { get; set; }

    /// <summary>
    /// Coordinates of the start point on the map (for the script).
    /// </summary>
    public Point Pos1 { get; set; }

    /// <summary>
    /// Coordinates of the end point on the map (for the script).
    /// </summary>
    public Point Pos2 { get; set; }

    /// <summary>
    /// List of found points (for the script).
    /// </summary>
    public List<Point> FoundPath { get; set; } = [];

    /// <summary>
    /// The coordinates of the point on the map. And the coordinates of the point on the map where the Npc goes.
    /// </summary>
    public Point Position { get; set; }

    /// <summary>
    /// Path length from the start (G).
    /// </summary>
    private float PathLengthFromStart { get; set; }

    /// <summary>
    /// The point from which it came to this point.
    /// </summary>
    private PathNode CameFrom { get; set; }

    /// <summary>
    /// Approximate distance to target (H).
    /// </summary>
    private float HeuristicEstimatePathLength { get; init; }

    /// <summary>
    /// Expected total distance to target (F).
    /// </summary>
    private float EstimateFullPathLength => PathLengthFromStart + HeuristicEstimatePathLength;

    /// <summary>
    /// Basic method of route calculation.
    /// </summary>
    /// <param name="world"></param>
    /// <param name="start"></param>
    /// <param name="goal"></param>
    /// <returns></returns>
    public List<Point> FindPath(WorldInstance world, Point start, Point goal)
    {
        // Step 0
        FoundPath = [];
        // Find the nearest point from the start point in the list of geodata points and start the search from it.
        var (current, posStart) = world.Template.GeoData.FindСlosestToTheCurrent(ZoneKey, new Vector3(start.X, start.Y, start.Z));
        start = posStart; // replace it with the nearest point from the geodata
        var (_, posEnd) = world.Template.GeoData.FindСlosestToTheCurrent(ZoneKey, new Vector3(goal.X, goal.Y, goal.Z));
        goal = posEnd; // replace it with the nearest point from the geodata

        // Step 1.
        var closedSet = new Collection<PathNode>();
        var openSet = new Collection<PathNode>();

        // Step 2.
        var startNode = new PathNode
        {
            Current = current,
            Position = start,
            CameFrom = null,
            PathLengthFromStart = 0,
            HeuristicEstimatePathLength = GetHeuristicPathLength(start)
        };
        openSet.Add(startNode);

        while (openSet.Count > 0)
        {
            // Step 3.
            var currentNode = openSet.OrderBy(node => node.EstimateFullPathLength).First();

            // Step 4.
            if (currentNode.Position.Equals(goal))
            {
                var result = GetPathForNode(currentNode);
                // Leave the nearest point taken from geodata instead of the point from where we are going
                // result[0] = pos1; // replace the first and the last point with the real one
                // result[^1] = pos2;
                // Let's add the target coordinates to the found points
                result.Add(Pos2);
                result = AiGeoDataManager.DouglasPeuckerReduction(result, 2.0);
                Position = result[0];
                Current = 0;
                return result;
            }

            // Step 5.
            openSet.Remove(currentNode);
            closedSet.Add(currentNode);

            // Step 6.
            foreach (var neighbourNode in GetNeighbours(world, ZoneKey, currentNode))
            {
                // Step 7.
                if (closedSet.Any(node => node.Position.Equals(neighbourNode.Position)))
                {
                    continue;
                }

                var openNode = openSet.FirstOrDefault(node => node.Position.Equals(neighbourNode.Position));
                // Step 8.
                if (openNode == null)
                {
                    openSet.Add(neighbourNode);
                }
                else if (openNode.PathLengthFromStart > neighbourNode.PathLengthFromStart)
                {
                    // Step 9.
                    openNode.CameFrom = currentNode;
                    openNode.PathLengthFromStart = neighbourNode.PathLengthFromStart;
                }
            }
        }
        // Step 10.
        return [];
    }

    /// <summary>
    /// G: Function for the distance from the starting point to the current point.
    /// </summary>
    /// <param name="to"></param>
    /// <returns></returns>
    private float GetDistanceFromStart(Point to)
    {
        var fromVector = new Vector3(Pos1.X, Pos1.Y, Pos1.Z);
        var toVector = new Vector3(to.X, to.Y, to.Z);
        return MathUtil.CalculateDistance(fromVector, toVector);
    }

    /// <summary>
    /// H: Estimates the distance to the target.
    /// </summary>
    /// <param name="from"></param>
    /// <returns></returns>
    private float GetHeuristicPathLength(Point from)
    {
        // point-to-point distance
        var fromVector = new Vector3(from.X, from.Y, from.Z);
        var toVector = new Vector3(Pos2.X, Pos2.Y, Pos2.Z);
        return MathUtil.CalculateDistance(fromVector, toVector);
    }

    /// <summary>
    /// Obtaining a list of neighbors
    /// </summary>
    /// <param name="world"></param>
    /// <param name="zoneKey"></param>
    /// <param name="pathNode"></param>
    /// <returns></returns>
    private Collection<PathNode> GetNeighbours(WorldInstance world, uint zoneKey, PathNode pathNode)
    {
        var result = new Collection<PathNode>();

        // The adjacent points are the points where you can go.
        var neighbourPoints = world.Template.GeoData.GetAvailablePoints(zoneKey, pathNode.Current);

        foreach (var point in neighbourPoints)
        {
            // Checking that the point falls within the forbidden area where it is not allowed to walk.
            if (world.Template.GeoData.CheckImpossibleWalk(point.Position))
            {
                //ViewPoint(point.Position, 858u); // let's show the point for debugging purposes
                continue;
            }

            // Fill in the data for the waypoint.
            var neighbourNode = new PathNode
            {
                Current = point.EndPoint,
                Position = point.Position,
                CameFrom = pathNode,
                PathLengthFromStart = GetDistanceFromStart(point.Position),
                HeuristicEstimatePathLength = GetHeuristicPathLength(point.Position)
            };

            result.Add(neighbourNode);
        }

        return result;
    }

    /// <summary>
    /// Obtaining a route. The route is represented as a list of point coordinates.
    /// </summary>
    /// <param name="pathNode"></param>
    /// <returns></returns>
    private static List<Point> GetPathForNode(PathNode pathNode)
    {
        var result = new List<Point>();
        var currentNode = pathNode;
        while (currentNode != null)
        {
            result.Add(currentNode.Position);
            //ViewPoint(currentNode.Position, 5014u); // let's show the point for debugging purposes
            currentNode = currentNode.CameFrom;
        }
        result.Reverse();

        return result;
    }
}
