namespace AAEmu.Game.Models.Game.World.Transform
{
    public class WorldSpawnPosition
    {
        public uint WorldId { get; set; } = 0;
        public uint ZoneId { get; set; } = 0;

        public float X { get; set; } = 0f;
        public float Y { get; set; } = 0f;
        public float Z { get; set; } = 0f;

        /// <summary>
        /// Rotation around Z-Axis in radians
        /// </summary>
        public float Yaw { get; set; } = 0f;
        /// <summary>
        /// Rotation around Y-Axis in radians
        /// </summary>
        public float Pitch { get; set; } = 0f;
        /// <summary>
        /// Rotation around X-Axis in radians
        /// </summary>
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
    }
}
