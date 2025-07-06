using System.Collections.Generic;
using Newtonsoft.Json;

namespace AAEmu.WorldConverter.Models
{
    public class JWorld
    {
        public byte Id { get; set; }

        public string Name { get; set; }

        // public float OffsetX { get; set; }
        // public float OffsetY { get; set; }
        public float MaxHeight { get; set; }
        public double HeightMaxCoeff { get; set; }
        public float OceanLevel { get; set; }
        public int CellX { get; set; }
        public int CellY { get; set; }

        [JsonIgnore] public uint HeighestPoint { get; set; }
        [JsonIgnore] public List<Zone> Zones { get; set; }
        [JsonIgnore] public Hmap[,] HeightMap { get; set; }
        [JsonIgnore] public ushort[,] HeightMapRaw { get; set; }
    }

    public class Zone
    {
        public ushort Id { get; set; }
        public string Name { get; set; }
        public List<Cell> Cells { get; set; }
    }

    public class Cell
    {
        public int X { get; set; }
        public int Y { get; set; }
        public List<Sector> Sectors { get; set; }
    }

    public class Sector
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}
