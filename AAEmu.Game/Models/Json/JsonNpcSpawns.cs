using System.Collections.Generic;

namespace AAEmu.Game.Models.Json
{
    public class Pos
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public sbyte RotationX { get; set; }
        public sbyte RotationY { get; set; }
        public sbyte RotationZ { get; set; }
    }

    public class JsonNpcSpawns
    {
        public uint Count { get; set; }
        public uint Id { get; set; }
        public uint UnitId { get; set; }
        public Pos Position { get; set; }
        public float Scale { get; set; }
    }
}
