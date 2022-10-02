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

        public short ComputedDelay { get; set; }

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

            stream.Write((short)(ComputedDelay / 10 + 10)); // TODO +10 It became visible flying arrows
            stream.Write((short)(_skill.Template.ChannelingTime / 10 + 10));
            stream.Write((byte)0); // f
            if (_skill.Template.Id != 2) // TODO: rotate between mainhand and offhand animation?
                stream.Write(_skill.Template.FireAnim?.Id ?? 0); // fire_anim_id 
            else
                stream.Write(2);

            stream.Write((byte)0); // flag

            return stream;
        }
    }
}
