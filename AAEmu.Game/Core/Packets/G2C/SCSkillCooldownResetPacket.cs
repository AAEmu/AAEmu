using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSkillCooldownResetPacket : GamePacket
    {
        private Character _chr;
        private uint _skillId;
        private uint _tagId;
        private bool _gcd;

        public SCSkillCooldownResetPacket() : base(SCOffsets.SCSkillCooldownResetPacket, 1)
        {
        }
        public SCSkillCooldownResetPacket(Character chr, uint skillId, uint tagId, bool gcd) : base(SCOffsets.SCSkillCooldownResetPacket, 1)
        {
            _skillId = skillId;
            _tagId = tagId;
            _chr = chr;
        }

        public override PacketStream Write(PacketStream stream)
        {
            //TODO заготовка для пакета

            stream.WriteBc(_chr.ObjId); //unitId
            stream.Write(_skillId); //skillId
            stream.Write(_tagId); //tagId
            stream.Write(_gcd); //gcd - Trigger GCD

            return stream;
        }
    }
}
