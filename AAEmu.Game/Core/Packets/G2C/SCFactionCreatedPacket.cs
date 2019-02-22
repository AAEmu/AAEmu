using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFactionCreatedPacket : GamePacket
    {
        private readonly SystemFaction _faction;
        private readonly uint _ownerObjId;
        private readonly (uint memberObjId, uint memberId, string name)[] _members;

        public SCFactionCreatedPacket(SystemFaction faction, uint ownerObjId, (uint memberObjId, uint memberId, string name)[] members) :
            base(SCOffsets.SCFactionCreatedPacket, 1)
        {
            _faction = faction;
            _ownerObjId = ownerObjId;
            _members = members;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_faction.Id);
            stream.Write(_faction.AggroLink);
            stream.Write(_faction.MotherId);
            stream.Write(_faction.Name);
            stream.Write(_faction.OwnerId);
            stream.Write(_faction.OwnerName);
            stream.Write(_faction.UnitOwnerType);
            stream.Write(_faction.PoliticalSystem);
            stream.Write(_faction.Created);
            stream.Write(_faction.DiplomacyTarget);
            stream.Write((byte)0); // allowChangeName

            stream.WriteBc(_ownerObjId);
            stream.Write((byte)_members.Length); // TODO max length 4
            foreach (var (objId, id, name) in _members)
            {
                stream.WriteBc(objId);
                stream.Write(id);
                stream.Write(name);
            }

            return stream;
        }
    }
}
