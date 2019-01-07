using System;

namespace AAEmu.Game.Models.Game.Faction
{
    public class FactionRelation
    {
        public uint Id { get; set; }
        public uint Id2 { get; set; }
        public RelationState State { get; set; }
        public DateTime ExpTime { get; set; }
    }
}