using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSkillStartedPacket : GamePacket
    {
        private readonly uint _id;
        private readonly ushort _tl;
        private readonly SkillAction _caster;
        private readonly SkillAction _target;
        private readonly Skill _skill;

        public SCSkillStartedPacket(uint id, ushort tl, SkillAction caster, SkillAction target, Skill skill) : base(0x09a, 1)
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
            // TODO flag blok
            stream.Write((short) (_skill.Template.CastingTime / 10));
            stream.Write((short) (_skill.Template.CastingTime / 10));
            stream.Write((short) 0); // castSynergy
            stream.Write((byte) 0); // f
            return stream;
        }
    }
}