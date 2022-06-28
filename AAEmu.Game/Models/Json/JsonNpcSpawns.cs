using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AAEmu.Game.Models.Json
{
    public class Pos
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        //[JsonIgnore]
        //public sbyte RotationX { get; set; }
        //[JsonIgnore]
        //public sbyte RotationY { get; set; }
        //[JsonIgnore]
        //public sbyte RotationZ { get; set; }
        //public float Roll { get; set; }
        //public float Pitch { get; set; }
        public float Yaw { get; set; }
    }

    public class JsonNpcSpawns
    {
        //[JsonIgnore]
        //public uint Count { get; set; }
        public uint Id { get; set; }
        public uint UnitId { get; set; }
        public Pos Position { get; set; }
        //[JsonIgnore]
        //public float Scale { get; set; }
    }
}
