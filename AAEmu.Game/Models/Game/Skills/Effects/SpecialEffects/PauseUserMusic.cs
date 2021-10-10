using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

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
            // TODO: Not sure how pause used to work back in 1.2, it currently just behaves like stop
            _log.Warn("Special effects: PauseUserMusic");
            target.BroadcastPacket(new SCPauseUserMusicPacket(target.ObjId), true);
            
            // Remove active playing buff effects
            var b = target.Buffs;
            var allMusicBuffs = SkillManager.Instance.GetBuffsByTagId((uint)TagsEnum.PlaySong); // 1155 = Play Song
            foreach (var buff in allMusicBuffs)
            {
                if (b.CheckBuff(buff))
                    b.RemoveBuff(buff);
            }
            
        }
    }
}
