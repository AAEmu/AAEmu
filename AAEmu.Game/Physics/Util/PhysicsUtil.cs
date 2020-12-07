using System;
using System.Numerics;
using AAEmu.Game.Utils;
using Jitter.LinearMath;

namespace AAEmu.Game.Physics.Util
{
    public class PhysicsUtil
    {
        public static Quaternion JMatrixToQuaternion(JMatrix matrix)
        {
            var jq = JQuaternion.CreateFromMatrix(matrix);
            
            return new Quaternion()
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
            
            var roll  = (float) Math.Atan2(2*q.Y*q.W - 2*q.X*q.Z, 1 - 2*q.Y*q.Y - 2*q.Z*q.Z);
            var pitch = (float) Math.Atan2(2*q.X*q.W - 2*q.Y*q.Z, 1 - 2*q.X*q.X - 2*q.Z*q.Z);
            var yaw   = (float) Math.Asin(2*q.X*q.Y + 2*q.Z*q.W);

            return (roll, pitch, yaw);
        }
    }
}
