using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Models.Game.Expeditions
{
    public class Expedition : SystemFaction
    {
        public List<Member> Members { get; set; }
        public List<ExpeditionRolePolicy> Policies { get; set; }
    }
}
