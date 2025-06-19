using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class AddExp : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.AddExp;

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
        if (caster is Character) { Logger.Debug("Special effects: AddExp value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

        if (target is not Unit unit)
        {
            return;
        }

        var expToAdd = value1;

        if (expToAdd == 0 && unit.Level >= 50) // Experia
        {
            var expBySkillEffectForLevel = FormulaManager.Instance.GetFormula((uint)FormulaKind.ExpBySkillEffect);
            var res = expBySkillEffectForLevel.Evaluate(new Dictionary<string, double>() { ["pc_level"] = unit.Level });

            expToAdd = (int)(res * (value3 / 10.0f));
        }

        switch (target)
        {
            case Units.Mate mate:
                if (mate.IsMaxLevel)
                {
                    // Cancel the skill and don't consume the item/reagent (e.g. Companion Crust)
                    skill.Cancelled = true;
                    ((Unit)caster).SendErrorMessage(ErrorMessageType.InvalidTarget);
                    // TODO: This doesn't stop the skill on the client, so it still performs the animation
                }
                else
                {
                    mate.AddExp(expToAdd);
                }
                break;
            case Character character:
                character.AddExp(expToAdd, true);
                break;
        }
    }
}
