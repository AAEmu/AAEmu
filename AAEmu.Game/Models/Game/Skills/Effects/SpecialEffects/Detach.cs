using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class Detach : SpecialEffectAction
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

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
            if (caster is Character chara)
            {
                //maybe we should check what were attached to?
                //MateManager.Instance.UnMountMate(chara, skill.TlId, ap, DetachUnitReason.UnmountMate);
            }
            _log.Warn("value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);
        }
    }

    enum DetachUnitReason : byte
    {
        None = 0x0,
        Death = 0x1,
        KnockBack = 0x2,
        RagDoll = 0x3,
        UseMountSkill = 0x4,
        ForciblyUnbindSlave = 0x5,
        UnboardTransfer = 0x6,
        ForciblyByServer = 0x7,
        BeginCutscene = 0x8,
        UnmountMate = 0x9,
        DismissPet = 0xA,
        KickByMaster = 0xB,
        KickByDespawn = 0xC,
        ScheduleToLogout = 0xD,
        ReAttach = 0xE
    }
}
