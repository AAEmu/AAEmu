using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class FamilyMembers
    {
        public uint CharacterId { get; set; }
        public uint FamilyId { get; set; }
        public string Name { get; set; }
        public byte Role { get; set; }
        public string Title { get; set; }
    }
}
