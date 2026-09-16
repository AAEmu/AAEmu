using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// The types E7 gave an action class. <c>SpecialEffect</c> resolves its action by name
/// (<c>AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects.&lt;SpecialType&gt;</c>), so a class that is
/// renamed, moved or never added is a cast that logs "Unknown special effect" and then does nothing - and
/// <c>Skill.IsPureNoOpCast</c> reads the same answer to decide whether the cast may be charged its
/// reagents. Asserting the resolution here is what keeps the two in step.
/// </summary>
public class ImplementedSpecialEffectsTests
{
    [Test]
    [Arguments(SpecialType.MoveToSavedPos)]        // 172, 9 rows, all used by 급습 / 마법진 이동
    [Arguments(SpecialType.ChangeBuffToleranceStep)] // 157, 19 rows, 39373 on 40364 결투를 위하여
    [Arguments(SpecialType.ChargeCooldown)]        // 158, 13 rows, all dormant
    [Arguments(SpecialType.ChangeChargeSkillCount)] // 166, 5 rows, 35202 on 37430
    [Arguments(SpecialType.ChangeChargeCooldown)]  // 167, 35203 on 37272
    [Arguments(SpecialType.ZoneConflictChange)]    // 170, 22 rows across 19 war/peace declarations
    public async Task TheType_ResolvesToAnActionClass(SpecialType specialType)
    {
        await Assert.That(SpecialEffect.IsImplemented(specialType)).IsTrue();
    }
}
