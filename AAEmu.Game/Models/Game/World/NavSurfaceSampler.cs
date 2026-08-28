using System.Numerics;

using AAEmu.Game.Models.CryEngine.Entities;
using AAEmu.Game.Models.CryEngine.Loaders;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Samples floor Z by projecting (x,y) onto nearby navmesh edges (not nearest vertex alone).
/// </summary>
public static class NavSurfaceSampler
{
    public const float DefaultMaxVerticalSep = 8f;
    public const float DefaultMaxXyRadius = 16f;

    public static float? TrySample(WorldTemplate worldTemplate, float x, float y, float zHint,
        float maxVerticalSep = DefaultMaxVerticalSep, float maxXyRadius = DefaultMaxXyRadius)
    {
        if (worldTemplate?.GeoData == null)
            return null;

        var bai = worldTemplate.GetBaiByPos(new Vector3(x, y, zHint));
        if (bai == null)
            return null;

        return TrySampleBai(bai, x, y, zHint, maxVerticalSep, maxXyRadius);
    }

    public static float? TrySampleBai(BaseBaiLoader bai, float x, float y, float zHint,
        float maxVerticalSep = DefaultMaxVerticalSep, float maxXyRadius = DefaultMaxXyRadius)
    {
        if (bai == null)
            return null;

        var bestZ = 0f;
        var bestDistSq = float.MaxValue;
        var found = false;
        var maxXyRadiusSq = maxXyRadius * maxXyRadius;

        foreach (var net in bai.NetMissionReaders)
        {
            foreach (var link in net.LinkDescriptorList)
            {
                var a = link.SourceNodeDescriptor;
                var b = link.TargetNodeDescriptor;
                if (a == null || b == null)
                    continue;

                // Drop edges whose endpoints are on a different floor than zHint
                if (MathF.Abs(a.Pos.Z - zHint) > maxVerticalSep && MathF.Abs(b.Pos.Z - zHint) > maxVerticalSep)
                    continue;

                if (!TryProjectOnEdgeXy(x, y, a.Pos, b.Pos, out var t, out var projX, out var projY, out var distSq))
                    continue;

                if (distSq > maxXyRadiusSq)
                    continue;

                var edgeZ = a.Pos.Z + (b.Pos.Z - a.Pos.Z) * t;
                if (MathF.Abs(edgeZ - zHint) > maxVerticalSep)
                    continue;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestZ = edgeZ;
                    found = true;
                }
            }
        }

        return found ? bestZ : null;
    }

    /// <summary>
    /// Project point onto edge AB in XY. t clamped to [0,1]. Returns false if edge has zero XY length.
    /// </summary>
    public static bool TryProjectOnEdgeXy(float x, float y, Vector3 a, Vector3 b,
        out float t, out float projX, out float projY, out float distSq)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var lenSq = abx * abx + aby * aby;
        if (lenSq < 1e-8f)
        {
            t = 0f;
            projX = a.X;
            projY = a.Y;
            var dx0 = x - a.X;
            var dy0 = y - a.Y;
            distSq = dx0 * dx0 + dy0 * dy0;
            return false;
        }

        t = ((x - a.X) * abx + (y - a.Y) * aby) / lenSq;
        if (t < 0f) t = 0f;
        else if (t > 1f) t = 1f;

        projX = a.X + abx * t;
        projY = a.Y + aby * t;
        var dx = x - projX;
        var dy = y - projY;
        distSq = dx * dx + dy * dy;
        return true;
    }

    /// <summary>
    /// Linear Z along edge by parameter t in [0,1].
    /// </summary>
    public static float LerpEdgeZ(NodeDescriptor a, NodeDescriptor b, float t)
    {
        return a.Pos.Z + (b.Pos.Z - a.Pos.Z) * t;
    }

    /// <summary>
    /// Rewrite path waypoint Z using edge projection (not raw graph vertex Z).
    /// Uses each point's previous corrected Z as the vertical hint so slopes stay coherent.
    /// Points with no nearby surface keep their original Z.
    /// </summary>
    public static List<Vector3> ApplyWaypointHeights(WorldTemplate worldTemplate, IEnumerable<Vector3> path,
        float maxVerticalSep = DefaultMaxVerticalSep, float maxXyRadius = DefaultMaxXyRadius)
    {
        var result = new List<Vector3>();
        if (path == null)
            return result;

        float? prevZ = null;
        foreach (var point in path)
        {
            var zHint = prevZ ?? point.Z;
            var surface = TrySample(worldTemplate, point.X, point.Y, zHint, maxVerticalSep, maxXyRadius);
            var z = surface ?? point.Z;
            result.Add(new Vector3(point.X, point.Y, z));
            prevZ = z;
        }

        return result;
    }

    /// <summary>
    /// Same as <see cref="ApplyWaypointHeights"/> but keeps a queue for AI path consumers.
    /// </summary>
    public static Queue<Vector3> ApplyWaypointHeightsQueue(WorldTemplate worldTemplate, IEnumerable<Vector3> path,
        float maxVerticalSep = DefaultMaxVerticalSep, float maxXyRadius = DefaultMaxXyRadius)
    {
        return new Queue<Vector3>(ApplyWaypointHeights(worldTemplate, path, maxVerticalSep, maxXyRadius));
    }
}
