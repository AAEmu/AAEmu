using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.World
{
    public class WorldSpawnPosition
    {
        public uint ZoneId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        // TODO: Add spawn rotation
    }
}
