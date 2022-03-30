using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCBuffCreatedPacket : GamePacket
    {
        private readonly Buff _effect;

        public SCBuffCreatedPacket(Buff effect) : base(SCOffsets.SCBuffCreatedPacket, 5)
        {
            _effect = effect;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_effect.SkillCaster);             // skillCaster
            stream.Write((_effect.Caster is Character character) ? character.Id : 0); // casterId (type)
            stream.WriteBc(_effect.Owner.ObjId);           // targetId
            stream.Write(_effect.Index);                   // buffId (type)
            
            // всё, что ниже, относится к WriteData
            stream.Write(_effect.Template.BuffId);         // buffId
            stream.Write(_effect.Caster.Level);            // sourceLevel
            stream.Write(_effect.AbLevel);                 // sourceAbLevel
            stream.Write(_effect.Skill?.Template.Id ?? 0); // skillId
            stream.Write(0);                               // stack add in 3.5.0.3
            _effect.WriteData(stream);
            /*
               sub_397ED240(v6);
               sub_397ED240(v2[3] / 0xAu);
               sub_397ED240(v2[4] / 0xAu);
               sub_397ED240(v2[5] / 0xAu);
             */
            return stream;
        }
    }
}
