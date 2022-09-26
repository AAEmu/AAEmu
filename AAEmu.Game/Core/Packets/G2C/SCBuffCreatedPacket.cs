﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCBuffCreatedPacket : GamePacket
    {
        private readonly Buff _buff;

        public SCBuffCreatedPacket(Buff buff) : base(SCOffsets.SCBuffCreatedPacket, 5)
        {
            _buff = buff;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_buff.SkillCaster);             // skillCaster
            stream.Write(_buff.Caster is Character character ? character.Id : 0); // casterId
            stream.WriteBc(_buff.Owner.ObjId);           // targetId
            stream.Write(_buff.Index);                   // buffId

            stream.Write(_buff.Template.BuffId);         // t template buffId
            stream.Write(_buff.Caster.Level);            // l sourceLevel
            stream.Write((ushort)_buff.AbLevel);         // a sourceAbLevel
            //TODO: Fix this applying CD to wrong skill
            if (_buff.Skill != null && _buff.Skill.Template.ToggleBuffId == _buff.Template.Id)
            {
                stream.Write(_buff.Skill.Template.Id); // s skillId
            }
            else
            {
                stream.Write(0);
            }

            stream.Write(0);                             // stack add in 3.0.3.0

            _buff.WriteData(stream);

            return stream;
        }
    }
}
