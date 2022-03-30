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
        private bool _rstc;
        private bool _rtstc;

        public SCSkillCooldownResetPacket() : base(SCOffsets.SCSkillCooldownResetPacket, 5)
        {
            
        }

        public SCSkillCooldownResetPacket(Character chr, uint skillId, uint tagId, bool gcd) : base(SCOffsets.SCSkillCooldownResetPacket, 5)
        {
            _skillId = skillId;
            _tagId = tagId;
            _gcd = gcd;
            _chr = chr;
            _gcd = gcd;

        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_chr.ObjId);
            stream.Write(_skillId); // skillType (type)
            stream.Write(_tagId);   // skillType (type)
            stream.Write(_gcd);     //gcd - Trigger GCD
            stream.Write(_rstc);    // rstc
            stream.Write(_rstc);    // rstc
            stream.Write(_rtstc);   // rtstc

            return stream;
        }
    }
}
