using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class CompletedQuests
    {
        public ushort Id { get; set; }
        public byte[] Data { get; set; }
        public uint Owner { get; set; }
    }
}
