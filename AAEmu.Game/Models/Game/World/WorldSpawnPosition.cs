using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.World
{
    public class WorldSpawnPosition
    {
        public uint WorldId { get; set; }
        public uint ZoneId { get; set; }

        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public float Yaw { get; set; }
        public float Pitch { get; set; }
        public float Roll { get; set; }

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
    }
}
