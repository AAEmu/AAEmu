using System.Collections.Generic;

using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Transfers
{
    public class Transfers
    {
        public string Name { get; set; }
        public int Type { get; set; }
        public int CellX { get; set; }
        public int CellY { get; set; }
        public List<Point> Pos { get; set; }

        public Transfers()
        {
            Pos = new List<Point>();
        }

    }
}
