using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class DisturbCasting : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.DisturbCasting;

    // Parameters are estimated to be :
    // value1 = chance in percent: 100 on 123 of the 126 reachable rows, 1 and 10 on two more, and 0 on
    //          skill 31183's row (20539), which that skill's own text says always clears casting - so 0
    //          reads as "no gate", not "never". The row counts are in CastInterruptRules.
    // value2 = set on 8 reachable rows (100..7000) with nothing in the content saying what it counts; not
    //          read. Postponing a cast instead of cancelling it belongs to the cast-delay formulas.
    public override void Execute(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill, SkillObject skillObject, DateTime time, int chance, int delay, int value3, int value4)
    {
        if (target is not Unit unit)
            return;

        // The chance covers the whole effect: a row that fails its roll disturbs nothing, which is how the
        // interrupt-only rows (방패치기, 주문 방해, 침묵) read at 100.
        if (!CastInterruptRules.RollSucceeds(chance, Random.Shared.Next(0, 100)))
            return;

        // A channeled skill is a plot timeline; the client's own stop asks the same thing of it.
        unit.ActivePlotState?.RequestCancellation();

        // An ordinary cast is the CastTask sitting on the unit.
        CastInterruptRules.TryInterrupt(unit);
    }
}
