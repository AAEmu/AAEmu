using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Faction
{
    public class SystemFaction : PacketMarshaler
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
        public byte AllowChangeName { get; set; }
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

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(AggroLink);
            stream.Write(MotherId);
            stream.Write(Name);
            stream.Write(OwnerId);
            stream.Write(OwnerName);
            stream.Write(UnitOwnerType);
            stream.Write(PoliticalSystem);
            stream.Write(Created);
            stream.Write(DiplomacyTarget);
            stream.Write(AllowChangeName);
            return stream;
        }
    }
}
