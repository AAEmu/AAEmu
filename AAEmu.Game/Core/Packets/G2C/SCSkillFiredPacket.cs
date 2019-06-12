using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSkillFiredPacket : GamePacket
    {
        private uint _id;
        private ushort _tl;
        private SkillCaster _caster;
        private SkillCastTarget _target;
        private SkillObject _skillObject;
        private Skill _skill;

        private short _effectDelay = 37;
        private int _fireAnimId = 2;
        private bool _dist;

        public SCSkillFiredPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject) : base(SCOffsets.SCSkillFiredPacket, 1)
        {
            _id = id;
            _tl = tl;
            _caster = caster;
            _target = target;
            _skill = skill;
            _skillObject = skillObject;
        }
        public SCSkillFiredPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject, short effectDelay = 37, int fireAnimId = 2, bool dist = true) : base(SCOffsets.SCSkillFiredPacket, 1)
        {
            _id = id;
            _tl = tl;
            _caster = caster;
            _target = target;
            _skill = skill;
            _skillObject = skillObject;
            _effectDelay = effectDelay;
            _fireAnimId = fireAnimId;
            _dist = dist;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_tl);
            stream.Write(_caster);
            stream.Write(_target);
            stream.Write(_skillObject);

            if (_id == 2 || _id == 3 || _id == 4)
            {
                if (_dist)
                {
                    stream.Write(_effectDelay);    // EffectDelay msec
                    stream.Write((short)(_skill.Template.ChannelingTime / 10)); // msec
                    stream.Write((byte)0);     // f
                    stream.Write(_fireAnimId); // fire_anim_id
                }
                else
                {
                    stream.Write((ushort)0);   // EffectDelay msec
                    stream.Write((ushort)0);   // ChannelingTime msec
                    stream.Write((byte)1);     // f
                    stream.Write((byte)15);    // c
                    stream.Write(_fireAnimId); // fire_anim_id
                }
            }
            else
            {
                stream.Write((short)(_skill.Template.EffectDelay / 10));
                stream.Write((short)(_skill.Template.ChannelingTime / 10));
                stream.Write((byte)0); // f
                stream.Write(_skill.Template.FireAnimId); // fire_anim_id
            }

            stream.Write((byte)0); // flag

            return stream;
        }
    }
}
