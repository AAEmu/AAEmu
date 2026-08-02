using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class AutoAttack : SpecialEffectAction
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

        // DB value1 is 0/1 — NOT a skill id (skill 1 does not exist). Old code treated
        // value1>0 as skillId and silently no-op'd value1=1.
        //
        // Skills with start_autoattack (e.g. Firebolt 10752) mean "hold to repeat THIS skill".
        // Their plots still embed an AutoAttack special; starting weapon skill 2 here makes the
        // character melee whenever the target is in staff range — wrong for casters.
        if (skill?.Template?.StartAutoAttack == true)
        {
            Logger.Debug("AutoAttack special skipped (source skill {0} has start_autoattack)", skill.Template.Id);
            return;
        }

        if (character.IsAutoAttack)
            return;

        var attackSkillId = ResolveWeaponAutoAttackSkillId(character);
        var attackTemplate = SkillManager.Instance.GetSkillTemplate(attackSkillId);
        if (attackTemplate == null)
            return;

        character.StartAutoSkill(new Skill(attackTemplate));
    }

    /// <summary>Mainhand/melee → 2; ranged weapon equipped → 4.</summary>
    private static uint ResolveWeaponAutoAttackSkillId(Character character)
    {
        var ranged = character.Equipment?.GetItemBySlot((int)EquipmentItemSlot.Ranged);
        if (ranged?.Template is WeaponTemplate)
            return 4;
        return 2;
    }
}
