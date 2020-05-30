namespace AAEmu.Game.Models.Game.World
{
    public class Point
    {
        public uint WorldId { get; set; }
        public uint ZoneId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public sbyte RotationX { get; set; }
        public sbyte RotationY { get; set; }
        public sbyte RotationZ { get; set; }
        
        public Point()
        {
        }

        public Point(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Point(float x, float y, float z, sbyte rotationX, sbyte rotationY, sbyte rotationZ)
        {
            X = x;
            Y = y;
            Z = z;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
        }

        public Point(uint zoneId, float x, float y, float z)
        {
            ZoneId = zoneId;
            X = x;
            Y = y;
            Z = z;
        }

        public Point(uint worldId, uint zoneId, float x, float y, float z,
            sbyte rotationX, sbyte rotationY, sbyte rotationZ)
        {
            WorldId = worldId;
            ZoneId = zoneId;
            X = x;
            Y = y;
            Z = z;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
        }

        public Point Clone()
        {
            return new Point(WorldId, ZoneId, X, Y, Z, RotationX, RotationY, RotationZ);
        }
    }
}
