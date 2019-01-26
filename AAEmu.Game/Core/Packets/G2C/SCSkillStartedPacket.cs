using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSkillStartedPacket : GamePacket
    {
        private readonly uint _id;
        private readonly ushort _tl;
        private readonly SkillCaster _caster;
        private readonly SkillCastTarget _target;
        private readonly Skill _skill;
        private readonly byte _flag;

        public SCSkillStartedPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill) : base(0x09a, 1)
        {
            _id = id;
            _tl = tl;
            _caster = caster;
            _target = target;
            _skill = skill;
            _flag = 0;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_tl);
            stream.Write(_caster);
            stream.Write(_target);
            stream.Write(_flag);
            if (_flag > 0)
            {
                var flagType = _flag & 15;
                switch (flagType)
                {
                    // TODO ...
                }
            }

            stream.Write((short)(_skill.Template.CastingTime / 10));
            stream.Write((short)(_skill.Template.CastingTime / 10));
            stream.Write((short)0); // castSynergy
            stream.Write((byte)0); // f
            return stream;
        }
    }
}
