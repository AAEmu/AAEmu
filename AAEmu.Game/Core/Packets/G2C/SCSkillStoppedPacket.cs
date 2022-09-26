using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSkillStoppedPacket : GamePacket
    {
        private readonly uint _unitObjId;
        private readonly uint _skillId;

        public SCSkillStoppedPacket(uint unitObjId, uint skillId) : base(SCOffsets.SCSkillStoppedPacket, 5)
        {
            _unitObjId = unitObjId;
            _skillId = skillId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            _log.Warn("SCSkillStoppedPacket: unitObjId = {0}, skillId = {1}", _unitObjId, _skillId);

            stream.WriteBc(_unitObjId); // unitId
            stream.Write(_skillId);     // skillType (type)

            return stream;
        }
    }
}
