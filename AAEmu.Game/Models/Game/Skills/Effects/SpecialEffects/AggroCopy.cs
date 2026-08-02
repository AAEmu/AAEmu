using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class AggroCopy : SpecialEffectAction
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
        if (caster is not Unit source || target is not Unit destination ||
            source.ObjId == destination.ObjId)
        {
            return;
        }

        if (WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.RelayAggroCopyToZone?.Invoke(source.ObjId, destination.ObjId);
            return;
        }

        destination.CopyAggroFrom(source);
    }
}
