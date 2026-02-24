// https://lsreg.ru/realizaciya-algoritma-poiska-a-na-c/

using System.Collections.ObjectModel;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.AStar;

/// <summary>
/// Reusable A* pathfinder.
/// NavMesh-first (smooth polygon pathfinding from terrain + brush geometry),
/// with BAI A* fallback for multi-layer scenarios (bridges, docks, 2nd floors).
/// </summary>
public class PathNode
{
    /// <summary>
    /// Current zone.Id
    /// </summary>
    public uint ZoneKey { get; set; }

    /// <summary>
    /// The current point on the map.
    /// </summary>
    public Vector3 CurrentTargetPos { get; set; }

    /// <summary>
    /// Coordinates of the start point on the map (for the script).
    /// </summary>
    public Vector3 StartPointPos { get; set; } = Vector3.Zero;

    /// <summary>
    /// Coordinates of the end point on the map (for the script).
    /// </summary>
    public Vector3 EndPointPos { get; set; } = Vector3.Zero;

    /// <summary>
    /// List of found points (for the script).
    /// </summary>
    public Queue<Vector3> FoundPath { get; set; } = [];

    /// <summary>
    /// The coordinates of the point on the map. And the coordinates of the point on the map where the Npc goes.
    /// </summary>
    public Vector3 Position { get; set; }

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
    private float PathLengthToEnd { get; init; }

    /// <summary>
    /// Expected total distance to target (F).
    /// </summary>
    private float EstimateFullPathLength => PathLengthFromStart + PathLengthToEnd;

    /// <summary>
    /// Basic method of route calculation.
    /// NavMesh-first (smooth polygon A* from terrain + brush collision geometry).
    /// Falls back to BAI A* for multi-layer scenarios or when NavMesh has no data.
    /// </summary>
    public List<Vector3> FindPath(WorldInstance world, Vector3 start, Vector3 goal)
    {
        // --- Strategy 1: NavMesh A* (primary) ---
        // DotRecast navmesh built from heightmap + brush collision meshes provides
        // smooth paths that naturally avoid walls, cliffs, and structures.
        // Single-layer limitation: cannot represent bridges over roads.
        if (world.NavMesh?.HasData == true)
        {
            // Multi-layer detection: if NPC's Z is significantly above/below the
            // NavMesh surface, it's on an elevated structure (bridge, dock, 2nd floor)
            // that NavMesh's single layer can't represent. Fall through to BAI.
            var navStartZ = world.NavMesh.GetHeight(start.X, start.Y, start.Z);
            var navGoalZ = world.NavMesh.GetHeight(goal.X, goal.Y, goal.Z);
            var startOnMesh = navStartZ > 0f && MathF.Abs(start.Z - navStartZ) < 5f;
            var goalOnMesh = navGoalZ > 0f && MathF.Abs(goal.Z - navGoalZ) < 5f;

            if (startOnMesh && goalOnMesh)
            {
                var navPath = world.NavMesh.FindPath(start, goal);
                if (navPath.Count > 0)
                {
                    EndPointPos = goal;
                    Position = navPath[0];
                    CurrentTargetPos = Vector3.Zero;
                    return navPath;
                }
            }
        }

        // --- Strategy 2: BAI A* (fallback) ---
        // Used when: NPC is on elevated layer (bridge/dock), NavMesh has no data,
        // or NavMesh.FindPath failed (disconnected mesh regions).
        var baiPath = FindPathBai(world, start, goal);
        if (baiPath.Count > 0)
        {
            var refined = RefinePathWithNavMesh(world, baiPath);
            EndPointPos = goal;
            Position = refined[0];
            CurrentTargetPos = Vector3.Zero;
            return refined;
        }

        // --- Strategy 3: NavMesh without multi-layer check (last resort) ---
        // If BAI also failed, try NavMesh anyway even with Z mismatch.
        // An imperfect path is better than no path at all.
        if (world.NavMesh?.HasData == true)
        {
            var navPath = world.NavMesh.FindPath(start, goal);
            if (navPath.Count > 0)
            {
                EndPointPos = goal;
                Position = navPath[0];
                CurrentTargetPos = Vector3.Zero;
                return navPath;
            }
        }

        return [];
    }

    /// <summary>
    /// Runs BAI A* pathfinding over the waypoint graph from netmission BAI files.
    /// Returns empty list if no BAI data exists near start/goal.
    /// </summary>
    private List<Vector3> FindPathBai(WorldInstance world, Vector3 start, Vector3 goal)
    {
        // Find the nearest BAI nodes to start and goal
        var posStart = world.Template.GeoData?.FindСlosestToTheCurrent(ZoneKey, start);
        var posEnd = world.Template.GeoData?.FindСlosestToTheCurrent(ZoneKey, goal);

        // No BAI data nearby — can't use BAI pathfinding
        if (posStart == null || posEnd == null)
            return [];

        // If nearest nodes are too far from actual positions, BAI coverage is poor here
        if ((posStart.Pos - start).Length() > 30f || (posEnd.Pos - goal).Length() > 30f)
            return [];

        var baiStart = posStart.Pos;
        var baiGoal = posEnd.Pos;
        EndPointPos = goal;
        var rawDistance = Vector3.Distance(baiStart, baiGoal);

        var closedSet = new Collection<PathNode>();
        var openSet = new Collection<PathNode>();

        var startNode = new PathNode
        {
            CurrentTargetPos = posStart.Pos,
            Position = baiStart,
            EndPointPos = baiGoal,
            CameFrom = null,
            PathLengthFromStart = 0,
            PathLengthToEnd = Vector3.Distance(baiStart, baiGoal)
        };
        openSet.Add(startNode);

        var maxLoopsLeft = (int)MathF.Ceiling(rawDistance * 10) + 50;
        while (openSet.Count > 0)
        {
            maxLoopsLeft--;

            var currentNode = openSet.OrderBy(node => node.EstimateFullPathLength).First();

            if (currentNode.Position.Equals(baiGoal) || maxLoopsLeft <= 0)
            {
                var result = GetPathForNode(currentNode);
                // Add the actual goal position (not the snapped BAI node)
                result.Add(goal);
                result = AiGeoDataManager.DouglasPeuckerReduction(result, 2.0);
                return result;
            }

            openSet.Remove(currentNode);
            closedSet.Add(currentNode);

            foreach (var neighbourNode in GetNeighbours(world, currentNode))
            {
                if (closedSet.Any(node => node.Position.Equals(neighbourNode.Position)))
                    continue;

                var openNode = openSet.FirstOrDefault(node => node.Position.Equals(neighbourNode.Position));
                if (openNode == null)
                {
                    openSet.Add(neighbourNode);
                }
                else if (openNode.PathLengthFromStart > neighbourNode.PathLengthFromStart)
                {
                    openNode.CameFrom = currentNode;
                    openNode.PathLengthFromStart = neighbourNode.PathLengthFromStart;
                }
            }
        }

        return [];
    }

    /// <summary>
    /// Refines a BAI waypoint path using DotRecast NavMesh for smoother movement.
    /// NavMesh includes all brush structures (stairs, ramps, walls, platforms),
    /// so raycast and FindPath work correctly across elevation changes.
    /// Only skips refinement for true multi-layer overlaps (bridge over road)
    /// where NavMesh is single-layer and can't represent both surfaces.
    /// </summary>
    private static List<Vector3> RefinePathWithNavMesh(WorldInstance world, List<Vector3> baiPath)
    {
        if (baiPath.Count < 2 || world.NavMesh?.HasData != true)
            return baiPath;

        var refined = new List<Vector3> { baiPath[0] };

        for (var i = 0; i < baiPath.Count - 1; i++)
        {
            var a = baiPath[i];
            var b = baiPath[i + 1];
            var dist2D = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

            // Try NavMesh raycast first — includes stairs, ramps, all brush geometry
            if (dist2D < 50f && world.NavMesh.Raycast(a, b))
            {
                // Clear line on NavMesh — just add endpoint (smooth direct movement)
                refined.Add(b);
                continue;
            }

            // NavMesh blocked or long segment: try NavMesh.FindPath for smooth sub-path
            if (dist2D > 5f)
            {
                var subPath = world.NavMesh.FindPath(a, b);
                if (subPath.Count > 1)
                {
                    // Validate sub-path endpoint reaches target Z reasonably.
                    // NavMesh includes stairs so large Z changes are expected.
                    // Only reject if endpoint Z is wildly off (wrong floor/layer).
                    var endpointZDiff = MathF.Abs(subPath[^1].Z - b.Z);
                    if (endpointZDiff < 5f)
                    {
                        // Skip first point of sub-path (it's ~= a, already in refined)
                        for (var j = 1; j < subPath.Count; j++)
                            refined.Add(subPath[j]);
                        continue;
                    }
                }
            }

            // Fallback: keep the BAI waypoint
            refined.Add(b);
        }

        return refined;
    }

    /// <summary>
    /// H: Estimates the distance to the target.
    /// </summary>
    private float GetHeuristicPathLength(Vector3 from)
    {
        return Vector3.Distance(from, EndPointPos);
    }

    /// <summary>
    /// Obtaining a list of neighbors from BAI graph edges.
    /// </summary>
    private Collection<PathNode> GetNeighbours(WorldInstance world, PathNode pathNode)
    {
        var result = new Collection<PathNode>();

        var bai = world.Template.GetBaiByPos(pathNode.CurrentTargetPos);
        if (bai == null)
            return result;

        var nearestNode = bai.FindClosestNetMissionNode(pathNode.CurrentTargetPos);
        if (nearestNode == null)
            return result;

        var neighbourPoints = world.Template.GeoData.GetAvailablePoints(nearestNode);

        foreach (var linkDescriptor in neighbourPoints)
        {
            if (linkDescriptor.TargetNodeDescriptor == null)
                continue;

            // Skip forbidden zones
            if (world.Template.GeoData.CheckImpossibleWalk(linkDescriptor.TargetNodeDescriptor.Pos))
                continue;

            var edgeLength = (linkDescriptor.SourceNodeDescriptor.Pos - linkDescriptor.TargetNodeDescriptor.Pos).Length();
            var neighbourNode = new PathNode
            {
                CurrentTargetPos = linkDescriptor.TargetNodeDescriptor.Pos,
                Position = linkDescriptor.TargetNodeDescriptor.Pos,
                EndPointPos = pathNode.EndPointPos,
                CameFrom = pathNode,
                PathLengthFromStart = pathNode.PathLengthFromStart + edgeLength,
                PathLengthToEnd = (linkDescriptor.TargetNodeDescriptor.Pos - pathNode.EndPointPos).Length()
            };

            result.Add(neighbourNode);
        }

        return result;
    }

    /// <summary>
    /// Reconstructs the path by tracing back from the goal node via CameFrom pointers.
    /// </summary>
    private static List<Vector3> GetPathForNode(PathNode pathNode)
    {
        var result = new List<Vector3>();
        var currentNode = pathNode;
        while (currentNode != null)
        {
            result.Add(currentNode.Position);
            currentNode = currentNode.CameFrom;
        }
        result.Reverse();

        return result;
    }
}
