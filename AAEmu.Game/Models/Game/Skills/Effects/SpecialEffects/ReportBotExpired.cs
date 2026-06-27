using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ReportBotExpired : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ReportBotExpired;

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
        if (caster is Character player)
        {
            Logger.Debug($"Special effects: ReportBotExpired value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}");
        }
        else
        {
            // Only applies to players
            return;
        }

        if (CrimeManager.Instance.ReportBotExpired(player))
        {
            Logger.Info($"Special effects: ReportBotExpired, {player.Name} ({player.Id}) removed their suspect buffs");
        }
    }
}
