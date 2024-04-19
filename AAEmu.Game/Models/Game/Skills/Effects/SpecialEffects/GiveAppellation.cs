using System;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class GiveAppellation : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.GiveAppellation;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int appellationId,
        int value2,
        int value3,
        int value4)
    {
        if (caster is Character character)
        {
            Logger.Debug($"Special effects: GiveAppellation value1 {appellationId}");
            character.Appellations.Add((uint)appellationId);
        }
    }
}
