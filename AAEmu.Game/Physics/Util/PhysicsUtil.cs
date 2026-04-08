using System.Numerics;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics.Util;

public class PhysicsUtil
{
    public static Quaternion JMatrixToQuaternion(JMatrix matrix)
    {
        var jq = JQuaternion.CreateFromMatrix(matrix);

        return new Quaternion
        {
            X = jq.X,
            Y = jq.Y,
            Z = jq.Z,
            W = jq.W
        };
    }

    public static (float, float, float) GetYawPitchRollFromJMatrix(JMatrix mat)
    {
        return MathUtil.GetYawPitchRollFromQuat(JMatrixToQuaternion(mat));
    }

    public static (float, float, float) GetYawPitchRollFromMatrix(JMatrix mat)
    {
        var q = JQuaternion.CreateFromMatrix(mat);

        var roll = (float)Math.Atan2(2 * q.Y * q.W - 2 * q.X * q.Z, 1 - 2 * q.Y * q.Y - 2 * q.Z * q.Z);
        var pitch = (float)Math.Atan2(2 * q.X * q.W - 2 * q.Y * q.Z, 1 - 2 * q.X * q.X - 2 * q.Z * q.Z);
        var yaw = (float)Math.Asin(2 * q.X * q.Y + 2 * q.Z * q.W);

        return (roll, pitch, yaw);
    }

    /// <summary>
    /// Jitter uses (X, Z) as horizontal and Y as up; game world uses (X, Y) as horizontal and Z as up
    /// (see <c>PhysicsManager.SyncTransformWithRigidBody</c>).
    /// </summary>
    public static float GetWaterSurfaceAtJitterPosition(WorldInstance world, JVector jitterPosition, out Vector3 flowDirection) =>
        world.Water.GetWaterSurface(jitterPosition.ToVector(), out flowDirection);

    public static float GetWaterSurfaceAtJitterPosition(WorldInstance world, float jitterX, float jitterYUp, float jitterZ, out Vector3 flowDirection) =>
        world.Water.GetWaterSurface(new JVector(jitterX, jitterYUp, jitterZ).ToVector(), out flowDirection);
}
