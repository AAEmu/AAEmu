using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class ExpeditionMembers
    {
        public uint CharacterId { get; set; }
        public uint ExpeditionId { get; set; }
        public string Name { get; set; }
        public byte Level { get; set; }
        public byte Role { get; set; }
        public DateTime LastLeaveTime { get; set; }
        public byte Ability1 { get; set; }
        public byte Ability2 { get; set; }
        public byte Ability3 { get; set; }
        public string Memo { get; set; }
    }
}
