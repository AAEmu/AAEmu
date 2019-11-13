using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class ExpeditionRolePolicies
    {
        public uint ExpeditionId { get; set; }
        public byte Role { get; set; }
        public string Name { get; set; }
        public byte DominionDeclare { get; set; }
        public byte Invite { get; set; }
        public byte Expel { get; set; }
        public byte Promote { get; set; }
        public byte Dismiss { get; set; }
        public byte Chat { get; set; }
        public byte ManagerChat { get; set; }
        public byte SiegeMaster { get; set; }
        public byte JoinSiege { get; set; }
    }
}
