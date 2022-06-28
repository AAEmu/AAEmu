using System.Collections.Generic;

namespace AAEmu.Game.Models.Json
{
    public class DoodadPos
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Roll { get; set; }
        public float Pitch { get; set; }
        public float Yaw { get; set; }
    }

    public class JsonDoodadSpawns
    {
        public uint Id { get; set; }
        public uint UnitId { get; set; }
        public DoodadPos Position { get; set; }
    }
}
