using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Newtonsoft.Json;

namespace UpdatesForTransform
{
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

        public NpcSpawnerNew()
        {
            Position = new WorldSpawnPositionNew();
        }
    }


}
