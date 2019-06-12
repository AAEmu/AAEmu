using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSkillStoppedPacket : GamePacket
    {
        private readonly uint _unitObjId;
        private readonly uint _skillId;

        public SCSkillStoppedPacket(uint unitObjId, uint skillId) : base(SCOffsets.SCSkillStoppedPacket, 1)
        {
            _unitObjId = unitObjId;
            _skillId = skillId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unitObjId);
            stream.Write(_skillId);
            return stream;
        }
    }
}
