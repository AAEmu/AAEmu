using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class SetVariable : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.SetVariable;

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
        if (caster is Character)
        {
            Logger.Debug(
                "Special effects: SetVariable index {0}, value {1}, operation {2}, value4 {3}",
                value1, value2, value3, value4);
        }

        var index = value1;
        var value = value2;
        var operation = value3;

        // Ops used by compact.sqlite3 special_effects (type set_variable):
        // 1  = add value2 to Variables[index]
        // 11 = assign value2 to Variables[index]
        // 12 = assign current plot hit count (LastEffectedTargetCount); value2 is always 0 in
        //      content. Used by "타겟 수 체크" area nodes (e.g. shotgun plot 5796 event 52251)
        //      before a Variable==0 "타겟 아무도 없음" gate. Without 12 the count stays 0 and
        //      every gun-path shot takes the no-target fail branch.
        if (skill?.ActivePlotState == null)
        {
            Logger.Error("No active plot state located.");
            return;
        }

        if (index < 0 || index >= skill.ActivePlotState.Variables.Length)
        {
            Logger.Error("SetVariable index {0} out of range", index);
            return;
        }

        switch (operation)
        {
            case 1:
                skill.ActivePlotState.Variables[index] += value;
                break;
            case 11:
                skill.ActivePlotState.Variables[index] = value;
                break;
            case 12:
                skill.ActivePlotState.Variables[index] = skill.ActivePlotState.LastEffectedTargetCount;
                break;
            default:
                Logger.Error("Invalid Plot Variable Operation Kind {0}.", operation);
                break;
        }
    }
}
