using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class Options
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public int Owner { get; set; }
    }
}
