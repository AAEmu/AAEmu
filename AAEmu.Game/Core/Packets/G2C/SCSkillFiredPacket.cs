using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSkillFiredPacket : GamePacket
    {
        private uint _id;
        private ushort _tl;
        private SkillAction _caster;
        private SkillAction _target;
        private Skill _skill;
        
        public SCSkillFiredPacket(uint id, ushort tl, SkillAction caster, SkillAction target, Skill skill) : base(0x09b, 1)
        {
            _id = id;
            _tl = tl;
            _caster = caster;
            _target = target;
            _skill = skill;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_tl);
            stream.Write(_caster);
            stream.Write(_target);
            stream.Write((byte) 0); // flag
            stream.Write((short)(_skill.Template.EffectDelay / 10));
            stream.Write((short)(_skill.Template.ChannelingTime / 10));
            stream.Write((byte)0); // f
            stream.Write(_skill.Template.FireAnimId); // fire_anim_id
            stream.Write((byte)0); // flag
            return stream;
        }
    }
}