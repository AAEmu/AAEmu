using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Plot GlobalCooldown value1==0 must not clear a press-armed shared GCD.
/// </summary>
public class GlobalCooldownRulesTests
{
    [Test]
    public async Task PlotZeroValue_LeavesAnArmedSharedGcdUnchanged()
    {
        var caster = new Unit { ObjId = 1 };
        var armed = DateTime.UtcNow.AddSeconds(2);
        caster.GlobalCooldown = armed;

        new GlobalCooldown().Execute(
            caster, null!, caster, null!, null!,
            new Skill { Template = new SkillTemplate { Id = 1, DefaultGcd = false } },
            null!, DateTime.UtcNow,
            value1: 0, value2: 0, value3: 0, value4: 0);

        await Assert.That(caster.GlobalCooldown).IsEqualTo(armed);
    }
}
