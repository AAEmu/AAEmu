using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.World
{
    public class ZoneConfig
    {
        public class CellConfig
        {
            public int X { get; set; }
            public int Y { get; set; }
            public List<SectorConfig> Sectors { get; set; }
        }

        public class SectorConfig
        {
            public int X { get; set; }
            public int Y { get; set; }
        }

        public ushort Id { get; set; }
        public string Name { get; set; }
        public List<CellConfig> Cells { get; set; }
    }
}