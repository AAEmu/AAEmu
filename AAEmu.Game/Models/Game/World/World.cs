using System;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Utils;

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

        public float GetRawHeightMapHeight(int x, int y)
        {
            // This is the old GetHeight()
            var sx = x / 2;
            var sy = y / 2;
            return (float)(HeightMaps[sx, sy] / HeightMaxCoefficient);
        }

        private System.Drawing.Rectangle FindNearestSignificantPoints(int x, int y)
        {
            return new System.Drawing.Rectangle(x - (x % 2), y - (y % 2), 2, 2);
        }

        public float GetHeight(float x, float y)
        {
            // return GetRawHeightMapHeight((int)x, (int)y); // <-- the old way we used to do things

            // Get bordering points
            var border = FindNearestSignificantPoints((int)Math.Floor(x), (int)Math.Floor(y));

            // Get heights for these points
            var heightTL = GetRawHeightMapHeight(border.Left, border.Top);
            var heightTR = GetRawHeightMapHeight(border.Right, border.Top);
            var heightBL = GetRawHeightMapHeight(border.Left, border.Bottom);
            var heightBR = GetRawHeightMapHeight(border.Right, border.Bottom);
            var offX = (x - border.Left) / 2;
            var offY = (y - border.Top) / 2;
            var height = MathUtil.Blerp(heightTL, heightTR, heightBL, heightBR, offX, offY); // bilinear interpolation

            return height;
        }

        public Region GetRegion(int x, int y)
        {
            if (!ValidRegion(x, y)) { return null; }
            if (Regions[x, y] == null) { return Regions[x, y] = new Region(Id, x, y); }
            return Regions[x, y];
        }

        public bool ValidRegion(int x, int y)
        {
            return x >= 0 && x < CellX * WorldManager.CELL_SIZE && y >= 0 && y < CellY * WorldManager.CELL_SIZE;
        }
    }
}
