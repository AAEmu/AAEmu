using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class Actabilities
    {
        public uint Id { get; set; }
        public int Point { get; set; }
        public byte Step { get; set; }
        public uint Owner { get; set; }
    }
}
