using System;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class PauseUserMusic : SpecialEffectAction
    {
        public override void Execute(Unit caster,
            SkillCaster casterObj,
            BaseUnit target,
            SkillCastTarget targetObj,
            CastAction castObj,
            Skill skill,
            SkillObject skillObject,
            DateTime time,
            int value1,
            int value2,
            int value3,
            int value4)
        {
            // TODO ...
            _log.Warn("Special effects: PauseUserMusic");
            target.BroadcastPacket(new SCPauseUserMusicPacket(target.ObjId), true);
            //target.Buffs.RemoveBuff(6176); // Flute Play
            //target.Buffs.RemoveBuff(6177); // Lute Play
        }
    }
}
