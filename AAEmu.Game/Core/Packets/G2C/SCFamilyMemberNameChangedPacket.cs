using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCFamilyMemberNameChangedPacket : GamePacket
    {
        private readonly uint _familyId;
        private readonly uint _charId;
        private readonly string _newName;
        
        public SCFamilyMemberNameChangedPacket(uint familyId, uint charId, string newName) : base(SCOffsets.SCFamilyMemberNameChangedPacket, 5)
        {
            _familyId = familyId;
            _charId = charId;
            _newName = newName;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_familyId);
            stream.Write(_charId);
            stream.Write(_newName);
            return stream;
        }
    }
}
