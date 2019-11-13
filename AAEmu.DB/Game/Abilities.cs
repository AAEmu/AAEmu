using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class Abilities
    {
        public byte Id { get; set; }
        public int Exp { get; set; }
        public uint Owner { get; set; }
    }
}
