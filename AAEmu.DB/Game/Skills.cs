using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class Skills
    {
        public uint Id { get; set; }
        public byte Level { get; set; }
        public string Type { get; set; }
        public int Owner { get; set; }
    }
}
