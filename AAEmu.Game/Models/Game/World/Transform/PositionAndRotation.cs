using System;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.World.Transform
{
    public class PositionAndRotation
    {
        public bool IsLocal { get; set; } = true;
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }

        private const float ToShortDivider = (1f / 32768f); // ~0.000030518509f ;
        private const float ToSByteDivider = (1f / 127f); // ~0.007874015748f ;
        private const float TwoPi = (MathF.PI * 2f);

        public PositionAndRotation()
        {
            Position = new Vector3();
            Rotation = new Vector3();
        }

        public PositionAndRotation(float posX, float posY, float posZ, float rotX, float rotY, float rotZ)
        {
            Position = new Vector3(posX, posY, posZ);
            Rotation = new Vector3(rotX, rotY, rotZ);
        }

        public PositionAndRotation(Vector3 position, Vector3 rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public PositionAndRotation Clone()
        {
            return new PositionAndRotation(Position.X, Position.Y, Position.Z, Rotation.X, Rotation.Y, Rotation.Z);
        }

        public Vector3 ToRollPitchYawDegrees()
        {
            return new Vector3(Rotation.X.RadToDeg(), Rotation.Y.RadToDeg(), Rotation.Z.RadToDeg());
        }

        public void SetPosition(float x, float y, float z)
        {
            Position = new Vector3(x, y, z);
        }

        public void SetPosition(Vector3 pos)
        {
            Position = pos;
        }

        public void SetHeight(float z)
        {
            Position = new Vector3(Position.X, Position.Y, z);
        }

        public void SetPosition(float x, float y, float z, float roll, float pitch, float yaw)
        {
            Position = new Vector3(x, y, z);
            Rotation = new Vector3(roll, pitch, yaw);
        }

        public (short, short, short) ToRollPitchYawShorts()
        {
            var q = Quaternion.CreateFromYawPitchRoll(Rotation.X, Rotation.Y, Rotation.Z);
            return ((short)(q.X * short.MaxValue), (short)(q.Y * short.MaxValue),
                (short)(q.Z * short.MaxValue));
        }

        public (sbyte, sbyte, sbyte) ToRollPitchYawSBytes()
        {
            var roll = (sbyte)(Rotation.X / TwoPi / ToSByteDivider);
            var pitch = (sbyte)(Rotation.Y / TwoPi / ToSByteDivider);
            var yaw = (sbyte)(Rotation.Z / TwoPi / ToSByteDivider);
            return (roll, pitch, yaw);
        }

        public (sbyte, sbyte, sbyte) ToRollPitchYawSBytesMovement()
        {
            sbyte roll = MathUtil.ConvertRadianToDirection(Rotation.X - TwoPi);
            sbyte pitch = MathUtil.ConvertRadianToDirection(Rotation.Y - TwoPi);
            sbyte yaw = MathUtil.ConvertRadianToDirection(Rotation.Z - TwoPi);
            /*
            sbyte roll = (sbyte)(vec3.X / (Math.PI * 2) / ToSByteDivider);
            sbyte pitch = (sbyte)(vec3.Y / (Math.PI * 2) / ToSByteDivider);
            sbyte yaw = (sbyte)(vec3.Z / (Math.PI * 2) / ToSByteDivider);
            */
            return (roll, pitch, yaw);
        }

        public void SetRotation(float roll, float pitch, float yaw)
        {
            Rotation = new Vector3(roll, pitch, yaw);
        }

        public void SetRotationDegree(float roll, float pitch, float yaw)
        {
            Rotation = new Vector3(roll.DegToRad(), pitch.DegToRad(), yaw.DegToRad());
        }

        /// <summary>
        /// Sets Yaw in Radian
        /// </summary>
        /// <param name="rotZ"></param>
        public void SetZRotation(float rotZ)
        {
            Rotation = new Vector3(Rotation.X, Rotation.Y, rotZ);
        }

        public void SetZRotation(short rotZ)
        {
            Rotation = new Vector3(Rotation.X, Rotation.Y,
                (float)MathUtil.ConvertDirectionToRadian(Helpers.ConvertRotation(rotZ)));
        }

        public void SetZRotation(sbyte rotZ)
        {
            Rotation = new Vector3(Rotation.X, Rotation.Y, (float)MathUtil.ConvertDirectionToRadian(rotZ));
        }

        /// <summary>
        /// Move position by a given offset
        /// </summary>
        /// <param name="offset">Amount to offset</param>
        public void Translate(Vector3 offset)
        {
            // TODO: Take into account isLocal = false ?
            Position += offset;
        }

        /// <summary>
        /// Move position by a given offset
        /// </summary>
        /// <param name="offsetX"></param>
        /// <param name="offsetY"></param>
        /// <param name="offsetZ"></param>
        public void Translate(float offsetX, float offsetY, float offsetZ) =>
            Translate(new Vector3(offsetX, offsetY, offsetZ));

        public void Rotate(Vector3 offset)
        {
            // Is this correct ?
            Rotation += offset;
        }

        public void Rotate(float roll, float pitch, float yaw)
        {
            // Is this correct ?
            Rotation += new Vector3(roll, pitch, yaw);
        }

        /// <summary>
        /// Moves Transform forward by distance units
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="useFullRotation">When true, takes into account the full rotation instead of just on the horizontal pane. (not implemented yet)</param>
        public void AddDistanceToFront(float distance, bool useFullRotation = false)
        {
            // TODO: Use Quaternion to do a proper InFront, currently height is ignored
            // TODO: Take into account IsLocal = false
            var off = new Vector3((-distance * (float)Math.Sin(Rotation.Z)), (distance * (float)Math.Cos(Rotation.Z)), 0);
            Translate(off);
        }

        /// <summary>
        /// Moves Transform to it's right by distance units
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="useFullRotation">When true, takes into account the full rotation instead of just on the horizontal pane. (not implemented yet)</param>
        public void AddDistanceToRight(float distance, bool useFullRotation = false)
        {
            // TODO: Use Quaternion to do a proper InFront, currently height is ignored
            // TODO: Take into account IsLocal = false
            var off = new Vector3((distance * (float)Math.Cos(Rotation.Z)), (distance * (float)Math.Sin(Rotation.Z)), 0);
            Translate(off);
        }

        /// <summary>
        /// Rotates Transform to make it face towards targetPosition's direction
        /// </summary>
        /// <param name="targetPosition"></param>
        public void LookAt(Vector3 targetPosition)
        {
            // TODO: Fix this as it's still wrong
            /*
            var forward = Vector3.Normalize(Position - targetPosition);
            var tmp = Vector3.Normalize(Vector3.UnitZ);
            var right = Vector3.Cross(tmp, forward);
            var up = Vector3.Cross(forward, right);
            var m = Matrix4x4.CreateLookAt(Position, targetPosition, up);
            var qr = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
            Rotation = qr;
            */
        }

        /// <summary>
        /// Clones current Position into a new Vector3
        /// </summary>
        /// <returns></returns>
        public Vector3 ClonePosition()
        {
            return new Vector3(Position.X, Position.Y, Position.Z);
        }

        public override string ToString()
        {
            return string.Format("X:{0:#,0.#} Y:{1:#,0.#} Z:{2:#,0.#}  r:{3:#,0.#}° p:{4:#,0.#}° y:{5:#,0.#}°",
                Position.X, Position.Y, Position.Z, Rotation.X.RadToDeg(), Rotation.Y.RadToDeg(),
                Rotation.Z.RadToDeg());
        }

        public bool IsOrigin()
        {
            return Position.Equals(Vector3.Zero);
        }

        /// <summary>
        /// Exports Rotation as a Quaternion
        /// </summary>
        /// <returns></returns>
        public Quaternion ToQuaternion() // yaw (Z), pitch (Y), roll (X)
        {
            return ToQuaternion(Rotation);
        }

        public static Quaternion ToQuaternion(Vector3 rotationVector3) // yaw (Z), pitch (Y), roll (X)
        {
            // Abbreviations for the various angular functions
            var cy = MathF.Cos(rotationVector3.Z * 0.5f);
            var sy = MathF.Sin(rotationVector3.Z * 0.5f);
            var cp = MathF.Cos(rotationVector3.Y * 0.5f);
            var sp = MathF.Sin(rotationVector3.Y * 0.5f);
            var cr = MathF.Cos(rotationVector3.X * 0.5f);
            var sr = MathF.Sin(rotationVector3.X * 0.5f);

            Quaternion q;
            q.W = cr * cp * cy + sr * sp * sy;
            q.X = sr * cp * cy - cr * sp * sy;
            q.Y = cr * sp * cy + sr * cp * sy;
            q.Z = cr * cp * sy - sr * sp * cy;

            return q;
        }


        /// <summary>
        /// Sets Rotation from Quaternion values
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <param name="w"></param>
        public static Vector3 FromQuaternion(float x, float y, float z, float w)
        {
            Vector3 angles;

            // roll (x-axis rotation)
            var sinRCosP = 2f * (w * x + y * z);
            var cosRCosP = 1f - 2f * (x * x + y * y);
            angles.X = MathF.Atan2(sinRCosP, cosRCosP);

            // pitch (y-axis rotation)
            var sinP = 2f * (w * y - z * x);
            angles.Y = MathF.Abs(sinP) >= 1f ? MathF.CopySign(TwoPi, sinP) : MathF.Asin(sinP);

            // yaw (z-axis rotation)
            var sinYCosP = 2f * (w * z + x * y);
            var cosYCosP = 1f - 2f * (y * y + z * z);
            angles.Z = MathF.Atan2(sinYCosP, cosYCosP);

            return angles;
        }

        public static Vector3 FromQuaternion(Quaternion q)
        {
            return FromQuaternion(q.X, q.Y, q.Z, q.W);
        }

        public void ApplyFromQuaternion(float x, float y, float z, float w)
        {
            Rotation = FromQuaternion(x, y, z, w);
        }

        /// <summary>
        /// Sets Rotation using a Quaternion
        /// </summary>
        /// <param name="q"></param>
        public void ApplyFromQuaternion(Quaternion q)
        {
            Rotation = FromQuaternion(q.X, q.Y, q.Z, q.W);
        }

    }
    
}
