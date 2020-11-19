using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCBuffCreatedPacket : GamePacket
    {
        private readonly Effect _effect;

        public SCBuffCreatedPacket(Effect effect) : base(SCOffsets.SCBuffCreatedPacket, 1)
        {
            _effect = effect;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_effect.SkillCaster);
            stream.Write((_effect.Caster is Character character) ? character.Id : 0); // casterId
            stream.WriteBc(_effect.Owner.ObjId); // targetBcId
            stream.Write(_effect.Index);
            stream.Write(_effect.Template.BuffId); // buffId
            stream.Write(_effect.Caster.Level); // sourceLevel
            stream.Write((short) _effect.AbLevel); // sourceAbLevel
            //TODO: Fix this applying CD to wrong skill
            //stream.Write(_effect.Skill?.Template.Id ?? 0); // skillId
            stream.Write(0); // skillId
            _effect.WriteData(stream);
            return stream;
        }
    }
}
