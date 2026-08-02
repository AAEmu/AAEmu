using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class GiveCrimePoint : SpecialEffectAction
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
        if (caster is not Character player)
            return;

        // special_effects.value1 is a signed i32 delta. The 10.0.2.13 content uses positive values
        // to add crime/infamy and negative values for prison work and reduction skills; value2-4 are
        // unused for every GiveCrimePoint row in the configured game-content database.
        player.AddCrime(value1);
        Logger.Debug($"Special effects: GiveCrimePoint applied {value1} to {player.Name} ({player.Id})");
    }
}
