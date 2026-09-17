using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// The rebuild a house is started into: the client's rebuild window casts the skill the chosen
/// <c>housing_rebuildings</c> row carries, at the house, and this effect hands that cast to
/// <see cref="HousingManager"/>.
/// </summary>
/// <remarks>
/// The effect's own values are zero on both shipped rows. The house is the cast's target. The
/// start skill is shared by every design in a pack, so the extra housing template (not the skill
/// alone) names the row.
/// </remarks>
public class RebuildHousing : SpecialEffectAction
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

        if (target is not House house)
        {
            Logger.Warn("RebuildHousing: cast {0} has no house as its target", skill?.Template?.Id ?? 0);
            return;
        }

        var rebuild = HousingGameData.Instance.GetRebuildTargetForCast(
            house.TemplateId,
            skill?.Template?.Id ?? 0,
            HousingRebuildSkillCast.RequestedHousingId(skillObject));
        if (rebuild == null)
        {
            Logger.Warn(
                "RebuildHousing: skill {0} housing {1} is not a rebuild this house offers",
                skill?.Template?.Id ?? 0,
                HousingRebuildSkillCast.RequestedHousingId(skillObject));
            return;
        }

        HousingManager.Instance.Rebuild(character, house, rebuild);
    }
}
