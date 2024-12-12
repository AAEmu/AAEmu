// https://lsreg.ru/realizaciya-algoritma-poiska-a-na-c/

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.AStar;

/// <summary>
/// Reusable A* path finder.
/// </summary>
public class PathNode
{
    // Current zone.Id
    public uint ZoneKey { get; set; }

    // The number of the current point on the map.
    public uint Current { get; set; }
    // Coordinates of the start point on the map (for the script).
    public Point pos1 { get; set; }

    // Coordinates of the end point on the map (for the script).
    public Point pos2 { get; set; }

    // List of found points (for the script).
    public List<Point> findPath { get; set; }

    // The coordinates of the point on the map. And the coordinates of the point on the map where the Npc goes.
    public Point Position { get; set; }

    // Path length from the start (G).
    public float PathLengthFromStart { get; set; }

    // The point from which it came to this point.
    public PathNode CameFrom { get; set; }

    // Approximate distance to target (H).
    public float HeuristicEstimatePathLength { get; set; }

    // Expected total distance to target (F).
    public float EstimateFullPathLength
    {
        get
        {
            return PathLengthFromStart + HeuristicEstimatePathLength;
        }
    }

    /// <summary>
    /// Basic method of route calculation.
    /// </summary>
    /// <param name="start"></param>
    /// <param name="goal"></param>
    /// <returns></returns>
    public List<Point> FindPath(Point start, Point goal)
    {
        // Step 0
        findPath = [];
        // Find the nearest point from the start point in the list of geodata points and start the search from it.
        var (current, posStart) = AiGeoDataManager.Instance.FindСlosestToTheCurrent(ZoneKey, new Vector3(start.X, start.Y, start.Z));
        start = posStart; // replace it with the nearest point from the geodata
        var (_, posEnd) = AiGeoDataManager.Instance.FindСlosestToTheCurrent(ZoneKey, new Vector3(goal.X, goal.Y, goal.Z));
        goal = posEnd; // replace it with the nearest point from the geodata

        // Step 1.
        var closedSet = new Collection<PathNode>();
        var openSet = new Collection<PathNode>();

        // Step 2.
        var startNode = new PathNode();
        startNode.Current = current;
        startNode.Position = start;
        startNode.CameFrom = null;
        startNode.PathLengthFromStart = 0;
        startNode.HeuristicEstimatePathLength = GetHeuristicPathLength(start);
        openSet.Add(startNode);

        while (openSet.Count > 0)
        {
            // Step 3.
            var currentNode = openSet.OrderBy(node => node.EstimateFullPathLength).First();

            // Step 4.
            if (currentNode.Position.Equals(goal))
            {
                var result = GetPathForNode(currentNode);
                // оставим ближайшую точку взятую с geodata вместо точки откуда идем
                //result[0] = pos1; // replace the first and the last point with the real one
                //result[^1] = pos2;
                // Добавим к найденным точкам координаты цели
                result.Add(pos2);
                result = AiGeoDataManager.DouglasPeuckerReduction(result, 2.0);
                Position = result[0];
                Current = 0;
                return result;
            }

            // Step 5.
            openSet.Remove(currentNode);
            closedSet.Add(currentNode);

            // Step 6.
            foreach (var neighbourNode in GetNeighbours(ZoneKey, currentNode, goal))
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
        ;
    }

    /// <summary>
    /// G: Function for the distance from the starting point to the current point.
    /// </summary>
    /// <returns></returns>
    private float GetDistanceFromStart(Point to)
    {
        var _from = new Vector3(pos1.X, pos1.Y, pos1.Z);
        var _to = new Vector3(to.X, to.Y, to.Z);
        return MathUtil.CalculateDistance(_from, _to, false);
    }

    /// <summary>
    /// H: Estimates the distance to the target.
    /// </summary>
    /// <param name="from"></param>
    /// <param name="to"></param>
    /// <returns></returns>
    private float GetHeuristicPathLength(Point from)
    {
        // point-to-point distance
        var _from = new Vector3(from.X, from.Y, from.Z);
        var _to = new Vector3(pos2.X, pos2.Y, pos2.Z);
        return MathUtil.CalculateDistance(_from, _to, false);
    }

    /// <summary>
    /// Obtaining a list of neighbors
    /// </summary>
    /// <param name="zoneKey"></param>
    /// <param name="pathNode"></param>
    /// <param name="goal"></param>
    /// <returns></returns>
    private Collection<PathNode> GetNeighbours(uint zoneKey, PathNode pathNode, Point goal)
    {
        var result = new Collection<PathNode>();

        // The adjacent points are the points where you can go.
        var neighbourPoints = AiGeoDataManager.Instance.GetAvailablePoints(zoneKey, pathNode.Current);

        foreach (var point in neighbourPoints)
        {
            // Checking that the point falls within the forbidden area where it is not allowed to walk.
            if (AiGeoDataManager.Instance.CheckImpossibleWalk(zoneKey, point.Position))
            {
                //ViewPoint(point.Position, 858u); // let's show the point for debugging purposes
                //continue;
            }

            // Fill in the data for the waypoint.
            var neighbourNode = new PathNode();
            neighbourNode.Current = point.EndPoint;
            neighbourNode.Position = point.Position;
            neighbourNode.CameFrom = pathNode;
            neighbourNode.PathLengthFromStart = GetDistanceFromStart(point.Position);
            neighbourNode.HeuristicEstimatePathLength = GetHeuristicPathLength(point.Position);

            result.Add(neighbourNode);
            //ViewPoint(point.Position, 681u); // let's show the point for debugging purposes
        }

        return result;
    }

    /// <summary>
    /// let's show the point for debugging purposes
    /// </summary>
    /// <param name="point"></param>
    /// <param name="templateId"></param>
    /* Unused
    private static void ViewPoint(Point point, uint templateId)
    {
        // 681 Pebble, 858	Contaminated Pebble, 5014 Combat Flag
        var unitTemplateId = templateId;

        var characters = WorldManager.Instance.GetAllCharacters();
        if (characters == null)
        {
            return;
        }
        var charPos = characters[0].Transform.CloneDetached();
        var doodadSpawner = new DoodadSpawner { Id = 0, UnitId = unitTemplateId };

        doodadSpawner.Position = charPos.CloneAsSpawnPosition();

        doodadSpawner.Position.X = point.X;
        doodadSpawner.Position.Y = point.Y;
        doodadSpawner.Position.Z = point.Z;
        ;
        doodadSpawner.Position.Yaw = 0;
        doodadSpawner.Position.Pitch = 0;
        doodadSpawner.Position.Roll = 0;

        doodadSpawner.Spawn(0, 0, characters[0].ObjId);
    }*/

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
