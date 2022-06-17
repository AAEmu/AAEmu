using System;
using System.ComponentModel;
using System.Numerics;

using AAEmu.Game.Core.Managers.World;

using Newtonsoft.Json;

namespace AAEmu.Game.Models.Game.World.Transform
{
    public class WorldSpawnPosition
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(1)] // Make sure to manualy change this when default world is changed
        public uint WorldId { get; set; } = WorldManager.DefaultWorldId;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(0)]
        public uint ZoneId { get; set; } = 0;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(0f)]
        public float X { get; set; } = 0f;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(0f)]
        public float Y { get; set; } = 0f;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(0f)]
        public float Z { get; set; } = 0f;

        /// <summary>
        /// Rotation around Z-Axis in radians
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(0f)]
        public float Yaw { get; set; } = 0f;

        /// <summary>
        /// Rotation around Y-Axis in radians
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(0f)]
        public float Pitch { get; set; } = 0f;

        /// <summary>
        /// Rotation around X-Axis in radians
        /// </summary>
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(0f)]
        public float Roll { get; set; } = 0f;

        private const double Tolerance = 0.1e-10;

        public WorldSpawnPosition Clone()
        {
            return new WorldSpawnPosition()
            {
                WorldId = WorldId,
                ZoneId = ZoneId,
                X = X,
                Y = Y,
                Z = Z,
                Yaw = Yaw,
                Pitch = Pitch,
                Roll = Roll
            };
        }

        /// <summary>
        /// Returns a Vector3 copy of the X Y and Z values
        /// </summary>
        /// <returns></returns>
        public Vector3 AsPositionVector()
        {
            return new Vector3(X, Y, Z);
        }

        public Quaternion AsRotationQuaternion()
        {
            return Quaternion.CreateFromYawPitchRoll(Yaw, Pitch, Roll);
        }

        public override string ToString()
        {
            return string.Format("X:{0:#,0.#} Y:{1:#,0.#} Z:{2:#,0.#}  r:{3:#,0.#}° p:{4:#,0.#}° y:{5:#,0.#}°", X, Y, Z, Roll, Pitch, Yaw);
        }

        public override bool Equals(object obj)
        {
            switch (obj)
            {
                case Vector2 vector2:
                    {
                        return Math.Abs(vector2.X - X) < Tolerance && Math.Abs(vector2.Y - Y) < Tolerance;
                    }
                case Vector3 vector3:
                    {
                        return Math.Abs(vector3.X - X) < Tolerance && Math.Abs(vector3.Y - Y) < Tolerance && Math.Abs(vector3.Z - Z) < Tolerance;
                    }
                case WorldSpawnPosition other:
                    return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
                default:
                    return false;
            }
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(WorldId, ZoneId, X, Y, Z);
        }

        public bool Equals(WorldSpawnPosition other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }
    }
}
