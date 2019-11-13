using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class Expeditions
    {
        public uint Id { get; set; }
        public uint Owner { get; set; }
        public string OwnerName { get; set; }
        public string Name { get; set; }
        public uint Mother { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
