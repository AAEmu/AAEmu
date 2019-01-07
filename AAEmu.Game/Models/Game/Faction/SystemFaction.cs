using System;
using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Faction
{
    public class SystemFaction
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public uint OwnerId { get; set; }
        public string OwnerName { get; set; }
        public uint MotherId { get; set; }
        public sbyte UnitOwnerType { get; set; }
        public byte PoliticalSystem { get; set; }
        public bool DiplomacyTarget { get; set; }
        public bool AggroLink { get; set; }
        public bool GuardHelp { get; set; }
        public DateTime Created { get; set; }

        public Dictionary<uint, FactionRelation> Relations { get; set; }

        public SystemFaction()
        {
            Relations = new Dictionary<uint, FactionRelation>();
        }

        public RelationState GetRelationState(uint id)
        {
            if (id == Id)
                return RelationState.Friendly;
            return Relations.ContainsKey(id) ? Relations[id].State : RelationState.Neutral;
        }
    }
}