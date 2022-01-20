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
            // Skill 22214 = Stop Playing (pressed pause)
            // Skill 22217 = Close the Score (pressed stop or end of song)
            _log.Trace("Special effects: PauseUserMusic -> {0}",
                skill?.Id == SkillsEnum.CloseTheScore ? "Stop" : "Pause");
            target.BroadcastPacket(new SCPauseUserMusicPacket(target.ObjId), true);

            // Check if stop was pressed. When at the end of the song, client also sends stop
            if (skill?.Id == SkillsEnum.CloseTheScore)
            {
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
}
