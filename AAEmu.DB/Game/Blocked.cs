using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class Blocked
    {
        public uint Owner { get; set; }
        public uint BlockedId { get; set; }
    }
}
