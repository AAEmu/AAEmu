using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Awards leadership - SpecialEffectType.GiveLeadershipPoint (175). This is the gameplay source the
/// Hero election runs on: the quests and PvP kills that grant leadership all reach it through here.
/// </summary>
/// <remarks>
/// Applied to the <em>target</em>, not the caster. Retail's leadership skills are cast on the person
/// being rewarded - a commander crediting a subordinate - and several award effects in this folder
/// that read <c>caster</c> do so because they are self-buffs. Getting this backwards would silently
/// pay the wrong player, so the target is resolved explicitly and non-players are ignored.
///
/// value1 is the amount. Negative values are refused rather than clamped: no shipped effect_special
/// row uses this to take leadership away (the nation-change penalty goes through
/// FormulaKind.LeadershipPointDecreaseForNationChange instead), so a negative here is far more likely
/// to be a bad data row than an intended deduction, and silently applying it would drain a stat the
/// player cannot easily rebuild.
/// </remarks>
public class GiveLeadershipPoint : SpecialEffectAction
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
        if (target is not Character character)
            return;

        if (value1 <= 0)
        {
            Logger.Warn("GiveLeadershipPoint: skill {0} carries a non-positive amount ({1}), ignoring",
                skill?.Template?.Id, value1);
            return;
        }

        var total = character.AddLeadership(value1);

        HeroManager.PublishLeadership(character);

        Logger.Debug("GiveLeadershipPoint: {0} +{1} leadership -> {2}", character.Name, value1, total);
    }
}
