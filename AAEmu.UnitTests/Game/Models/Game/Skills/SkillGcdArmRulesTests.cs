using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillGcdArmRulesTests
{
    [Test]
    public async Task AcceptedCast_ArmsSharedGcd()
    {
        await Assert.That(SkillGcdArmRules.ShouldArmOnAccept(bypassGcd: false, tlId: 1))
            .IsTrue();
    }

    [Test]
    public async Task SimulationBypass_DoesNotArm()
    {
        await Assert.That(SkillGcdArmRules.ShouldArmOnAccept(bypassGcd: true, tlId: 1))
            .IsFalse();
    }

    [Test]
    public async Task NoTimeline_MeansRefuse_DoesNotArm()
    {
        await Assert.That(SkillGcdArmRules.ShouldArmOnAccept(bypassGcd: false, tlId: 0))
            .IsFalse();
    }
}
