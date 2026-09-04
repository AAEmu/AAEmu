using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Completes a skillsaver apply after the client casts skill 32189.
/// Pending slot is stashed when <c>CSStartSkill</c> sees that skill id (from skill-object payload when present).
/// </summary>
public class ActivateSavedAbilitySet : SpecialEffectAction
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
        if (caster is not Character character)
            return;

        // Prefer skill-object slot. DB special-effect values are all 0 and must not mean "slot 0".
        if (character.AbilitySets.PendingActivationSlot < 0)
        {
            var fromObject = skillObject switch
            {
                SkillObjectAbilitySet abilitySet => abilitySet.SlotIndex,
                SkillObjectUnk5 unk5 => unk5.Step,
                SkillObjectUnk1 unk1 => unk1.Id,
                _ => -1
            };
            if (fromObject >= 0)
                character.AbilitySets.SetPendingActivationSlot(fromObject);
        }

        character.AbilitySets.TryActivatePending();
    }
}
