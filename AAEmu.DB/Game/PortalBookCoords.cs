using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class PortalBookCoords
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public uint ZoneId { get; set; }
        public int ZRot { get; set; }
        public uint SubZoneId { get; set; }
        public uint Owner { get; set; }
    }
}
