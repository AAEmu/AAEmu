using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;

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

        public RelationState GetRelationState(SystemFaction otherFaction)
        {
            if (otherFaction == null)
            {
                return RelationState.Neutral;
            }

            var factionId = MotherId != 0 ? MotherId : Id;
            var otherFactionId = otherFaction.MotherId != 0 ? otherFaction.MotherId : otherFaction.Id;

            if (factionId == otherFactionId)
            {
                return RelationState.Friendly;
            }

            //Not sure if we should prioritize mother faction here?
            if (MotherId != 0)
            {
                var motherFaction = FactionManager.Instance.GetFaction(MotherId);
                if (motherFaction != null)
                {
                    var motherRelations = motherFaction.Relations;
                    if (motherRelations.ContainsKey(otherFactionId))
                    {
                        return motherRelations[otherFactionId].State;
                    }

                    // TODO not found, so enemy (id = [1, 2, 3])
                    return RelationState.Hostile;
                }
            }

            return Relations.ContainsKey(otherFactionId) ? Relations[otherFactionId].State : RelationState.Neutral;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);                // type
            stream.Write(MotherId);          // type
            stream.Write(Name);              // name
            stream.Write(OwnerId);           // ownerId Int32
            stream.Write(OwnerName);         // ownerName
            stream.Write(UnitOwnerType);     // UnitOwnerType Byte
            stream.Write(PoliticalSystem);   // PoliticalSystem Byte
            stream.Write(Created);           // createdTime
            stream.Write(AggroLink);         // aggroLink
            stream.Write(true);              // dTarget
            //stream.Write(DiplomacyTarget); // нет в 3.0.3.0.
            stream.Write(AllowChangeName);   // allowChangeName
            stream.Write(0L);                // renameTime

            return stream;
        }
    }
}
