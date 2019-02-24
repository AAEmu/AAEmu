using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Game.World
{
    public class World
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public float MaxHeight { get; set; }
        public double HeightMaxCoefficient { get; set; }
        public float OceanLevel { get; set; }
        public int CellX { get; set; }
        public int CellY { get; set; }
        public Point SpawnPosition { get; set; }

        public Region[,] Regions { get; set; } // TODO ... world - okey, instance - ....
        public uint[,] ZoneIds { get; set; }
        public ushort[,] HeightMaps { get; set; }

        public float GetHeight(float x, float y)
        {
            var sx = (int) (x / 2);
            var sy = (int) (y / 2);
            var height = (float) (HeightMaps[sx, sy] / HeightMaxCoefficient);
            return height;
        }

        public Region GetRegion(int x, int y)
        {
            if (ValidRegion(x, y))
                if (Regions[x, y] == null)
                    return Regions[x, y] = new Region(Id, x, y);
                else
                    return Regions[x, y];

            return null;
        }

        public bool ValidRegion(int x, int y)
        {
            return x >= 0 && x < CellX * WorldManager.CELL_SIZE && y >= 0 && y < CellY * WorldManager.CELL_SIZE;
        }
    }
}
