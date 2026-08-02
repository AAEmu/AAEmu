using System.Numerics;

using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Terrain height lookup for a world template, sampled from the client's .bai node/vertex data
/// with the raw heightmap as fallback. Serves doodad placement and slave/transfer spawns; NPC
/// pathing and hull simulation are the Zone's responsibility and no longer query this class.
/// </summary>
public class GeoDataManager(WorldTemplate worldTemplate)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Floor height at a world position. Uses the nearest .bai navigation node or obstacle vertex
    /// when the cell has one, otherwise the raw heightmap sample.
    /// </summary>
    public float GetHeight(Vector3 pos)
    {
        try
        {
            var closestPoint = Vector3.Zero;
            var closestDistance = float.MaxValue;

            var bai = worldTemplate.GetBaiByPos(pos);
            if (bai != null)
            {
                foreach (var netMission in bai.NetMissionReaders)
                {
                    foreach (var (_, nodeDescriptor) in netMission.NodeDescriptorList)
                    {
                        var dist = (nodeDescriptor.Pos - pos).Length();
                        if (dist >= closestDistance)
                            continue;

                        closestDistance = dist;
                        closestPoint = nodeDescriptor.Pos;
                        if (closestDistance < 0.01f)
                            return closestPoint.Z;
                    }
                }

                foreach (var vertexMission in bai.VertexMissionReaders)
                {
                    foreach (var obstacleDataDescriptor in vertexMission.ObstacleDataDescriptorList)
                    {
                        var dist = (obstacleDataDescriptor.Pos - pos).Length();
                        if (dist >= closestDistance)
                            continue;

                        closestDistance = dist;
                        closestPoint = obstacleDataDescriptor.Pos;
                        if (closestDistance < 0.01f)
                            return closestPoint.Z;
                    }
                }
            }

            if (closestDistance >= float.MaxValue)
            {
                closestPoint = new Vector3(
                    pos.X,
                    pos.Y,
                    worldTemplate.GetRawHeightMapHeight((int)MathF.Round(pos.X), (int)MathF.Round(pos.Y)));
            }

            return closestPoint.Z;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "GetHeight failed at ({0:F1},{1:F1})", pos.X, pos.Y);
            return 0f;
        }
    }
}
