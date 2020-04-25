using AAEmu.Game.Core.Managers.World;
using System;

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
            var sx = (int)(x / 2);
            var sy = (int)(y / 2);
            return (float)(HeightMaps[sx, sy] / HeightMaxCoefficient);
        }

        public float GetHeight(float x, float y)
        {
            // return GetRawHeightMapHeight((int)x, (int)y);

            // Get bordering points
            var borderLeft = (int)Math.Floor(x);
            borderLeft -= (borderLeft % 2);
            // we're using a divider of 2 of the heightmaps in memory, so we need to compensate with that in mind (instead of 1)
            var borderRight = borderLeft + 2 ;
            var borderBottom = (int)Math.Floor(y);
            borderBottom -= (borderBottom % 2);
            var borderTop = borderBottom + 2 ;

            // Get heights for these points
            var heightTL = GetRawHeightMapHeight(borderLeft, borderTop);
            var heightTR = GetRawHeightMapHeight(borderRight, borderTop);
            var heightBL = GetRawHeightMapHeight(borderLeft, borderBottom);
            var heightBR = GetRawHeightMapHeight(borderRight, borderBottom);

            // %-based offset inside grid
            var offsetXL = (x - (float)borderLeft) / 2;
            var offsetYT = (y - (float)borderBottom) / 2;
            var deltaHeightXLeft = heightTL - heightBL ;
            var deltaHeightXRight = heightTR - heightBR ;
            
            // Calculate Height
            var heightXLeft = heightTL + (deltaHeightXLeft * offsetYT);
            var heightXRight = heightTR + (deltaHeightXRight * offsetYT);
            var height = heightXLeft + ((heightXRight - heightXLeft) * offsetXL);
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
