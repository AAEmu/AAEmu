using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUpdateSkillActiveTypePacket : GamePacket
    {
        private readonly uint _skillId;
        private readonly AbilityType _ability;

        public SCUpdateSkillActiveTypePacket(uint skillId, AbilityType ability) : base(SCOffsets.SCUpdateSkillActiveTypePacket, 5)
        {
            _skillId = skillId;
            _ability = ability;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_skillId);        // skillType (type)
            stream.Write((byte)_ability);  // ability

            return stream;
        }
    }
}
