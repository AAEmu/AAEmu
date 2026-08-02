using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class AggroReset : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
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
        if (target is not Unit affectedUnit)
            return;

        if (WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.RelayAggroResetToZone?.Invoke(
                affectedUnit.ObjId,
                value1,
                value2,
                value3,
                value4);
            return;
        }

        affectedUnit.ApplyAggroReset(value1, value2, value3, value4);
    }
}
