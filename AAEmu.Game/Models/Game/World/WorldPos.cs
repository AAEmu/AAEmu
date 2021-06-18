namespace AAEmu.Game.Models.Game.World
{
    public class WorldPos
    {
        public long X { get; set; }
        public long Y { get; set; }
        public float Z { get; set; }

        public WorldPos()
        {
        }

        public WorldPos(long x, long y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public WorldPos Clone()
        {
            return new WorldPos(X, Y, Z);
        }
    }
}
