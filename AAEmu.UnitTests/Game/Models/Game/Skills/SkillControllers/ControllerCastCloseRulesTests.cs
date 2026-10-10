using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.StreamAoi;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.SkillControllers;

/// <summary>
/// The decision that closes a controller cast which could not be realized. Such a cast used to be left
/// open, and the client kept the action — the player could not move, act, or leave the instance.
/// </summary>
public class ControllerCastCloseRulesTests
{
    [Test]
    public async Task ControllerCast_ThatCouldNotBeRealized_MustCloseNow()
    {
        // A leap with no target, or one outside its 6-20 m window, builds no controller.
        await Assert.That(SkillControllerRules.ControllerCastMustCloseNow(hasController: true, controllerRealized: false))
            .IsTrue();
    }

    [Test]
    public async Task ControllerCast_ThatWasRealized_IsLeftToItsNormalEnd()
    {
        await Assert.That(SkillControllerRules.ControllerCastMustCloseNow(hasController: true, controllerRealized: true))
            .IsFalse();
    }

    [Test]
    public async Task PlainCast_IsNeverClosedByThisRule()
    {
        // Most skills carry no controller at all; the rule must not touch them.
        await Assert.That(SkillControllerRules.ControllerCastMustCloseNow(hasController: false, controllerRealized: false))
            .IsFalse();
        await Assert.That(SkillControllerRules.ControllerCastMustCloseNow(hasController: false, controllerRealized: true))
            .IsFalse();
    }
}
