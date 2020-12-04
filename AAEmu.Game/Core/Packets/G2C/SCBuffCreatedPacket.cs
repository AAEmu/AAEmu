using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCBuffCreatedPacket : GamePacket
    {
        private readonly Buff _buff;

        public SCBuffCreatedPacket(Buff buff) : base(SCOffsets.SCBuffCreatedPacket, 1)
        {
            _buff = buff;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_buff.SkillCaster);
            stream.Write((_buff.Caster is Character character) ? character.Id : 0); // casterId
            stream.WriteBc(_buff.Owner.ObjId); // targetBcId
            stream.Write(_buff.Index);
            stream.Write(_buff.Template.BuffId); // buffId
            stream.Write(_buff.Caster.Level); // sourceLevel
            stream.Write((short) _buff.AbLevel); // sourceAbLevel
            //TODO: Fix this applying CD to wrong skill
            //stream.Write(_effect.Skill?.Template.Id ?? 0); // skillId\
            if (_buff.Skill != null && _buff.Skill.Template.ToggleBuffId == _buff.Template.Id)
                stream.Write(_buff.Skill.Template.Id); // skillId
            else
                stream.Write(0);
            _buff.WriteData(stream);
            return stream;
        }
    }
}
