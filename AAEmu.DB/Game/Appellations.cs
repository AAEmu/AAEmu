using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class Appellations
    {
        public int Id { get; set; }
        public byte Active { get; set; }
        public int Owner { get; set; }
    }
}
