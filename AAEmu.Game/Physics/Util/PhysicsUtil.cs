using System.Numerics;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics.Util;

/// <summary>
/// Physics helpers. The physics world uses the same axes as the game world:
/// X = east, Y = north, Z = up (gravity is -Z). A ship hull's local axes are
/// X = beam (starboard), Y = length (bow), Z = height, so a body's orientation
/// is the same rotation the game uses in <see cref="PositionAndRotation"/>.
/// </summary>
public class PhysicsUtil
{
    /// <summary>
    /// Game rotation (X = roll about the beam axis, Y = pitch about the length axis, Z = yaw) in radians,
    /// using the same Euler convention as <see cref="PositionAndRotation.FromQuaternion(float, float, float, float)"/>.
    /// </summary>
    public static JVector GetGameRotation(JQuaternion q)
    {
        return PositionAndRotation.FromQuaternion(q.X, q.Y, q.Z, q.W);
    }

    /// <summary>
    /// Yaw (rotation about world up, +Z) in radians. Yaw 0 faces +Y; positive yaw turns toward -X.
    /// </summary>
    public static float GetYaw(JQuaternion q)
    {
        return MathF.Atan2(2f * (q.W * q.Z + q.X * q.Y), 1f - 2f * (q.Y * q.Y + q.Z * q.Z));
    }

    /// <summary>
    /// Horizontal bow direction for <paramref name="yaw"/> (same as <see cref="PositionAndRotation.AddDistanceToFront"/>).
    /// </summary>
    public static (float X, float Y) GetForward(float yaw)
    {
        return (-MathF.Sin(yaw), MathF.Cos(yaw));
    }

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

}
