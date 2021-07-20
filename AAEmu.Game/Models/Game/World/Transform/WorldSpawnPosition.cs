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

        public WorldSpawnPosition Clone()
        {
            return new WorldSpawnPosition()
            {
                WorldId = this.WorldId,
                ZoneId = this.ZoneId,
                X = this.X,
                Y = this.Y,
                Z = this.Z,
                Yaw = this.Yaw,
                Pitch = this.Pitch,
                Roll = this.Roll
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
            return string.Format("X:{0:#,0.#} Y:{1:#,0.#} Z:{2:#,0.#}  r:{3:#,0.#}° p:{4:#,0.#}° y:{5:#,0.#}°",
                X, Y, Z, Roll, Pitch, Yaw);
        }
    }
}
