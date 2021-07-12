using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Newtonsoft.Json;

namespace UpdatesForTransform
{
    /// <summary>
    /// All default values are ignored to save space.
    /// Additionally this class has technically more fields than each individual json will require (adds all possible fields)
    /// </summary>
    public class WorldSpawnPositionNew
    {
        //public uint WorldId { get; set; }
        //public uint ZoneId { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(0f)]
        public float X { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(0f)]
        public float Y { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(0f)]
        public float Z { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(0f)]
        public float Roll { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(0f)]
        public float Pitch { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(0f)]
        public float Yaw { get; set; }
    }

    public class WorldSpawnPositionOld
    {
        //public uint WorldId { get; set; }
        //public uint ZoneId { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public float X { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public float Y { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public float Z { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public sbyte RotationX { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public sbyte RotationY { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public sbyte RotationZ { get; set; }
    }

    public class NpcSpawnerOld
    {
        public long Id;
        public long UnitId;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate, Required = Required.DisallowNull)]
        [DefaultValue(1)]
        public long Count = 1;
        public WorldSpawnPositionOld Position;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate, Required = Required.DisallowNull)]
        [DefaultValue(1f)]
        public float Scale = 1f;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate, Required = Required.DisallowNull)]
        [DefaultValue(0)]
        public long FuncGroupId = 0;
        public NpcSpawnerOld()
        {
            Position = new WorldSpawnPositionOld();
        }
    }

    public class NpcSpawnerNew
    {
        public long Id;
        public long UnitId;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate, Required = Required.DisallowNull)]
        [DefaultValue(1)]
        public long Count = 1;
        public WorldSpawnPositionNew Position;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate, Required = Required.DisallowNull)]
        [DefaultValue(1f)]
        public float Scale = 1f;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate, Required = Required.DisallowNull)]
        [DefaultValue(0)]
        public long FuncGroupId = 0;

        public NpcSpawnerNew()
        {
            Position = new WorldSpawnPositionNew();
        }
    }


}
