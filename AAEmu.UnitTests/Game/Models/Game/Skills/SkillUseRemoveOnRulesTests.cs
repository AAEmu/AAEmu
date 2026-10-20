using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillUseRemoveOnRulesTests
{
    [Test]
    public async Task PlayerSkillCast_TriggersRemoveOnUseSkill()
    {
        await Assert.That(SkillUseRemoveOnRules.ShouldTriggerRemoveOnUseSkill(new CastSkill(10723, 1)))
            .IsTrue();
    }

    [Test]
    public async Task PlotCast_TriggersRemoveOnUseSkill()
    {
        await Assert.That(SkillUseRemoveOnRules.ShouldTriggerRemoveOnUseSkill(new CastPlot(1, 1, 1, 10723)))
            .IsTrue();
    }

    [Test]
    public async Task BuffTickCast_DoesNotTriggerRemoveOnUseSkill()
    {
        // Cadence managers tick SkillUse with CastBuff — must not wipe remove_on_use_skill songs.
        await Assert.That(SkillUseRemoveOnRules.ShouldTriggerRemoveOnUseSkill(new CastBuff(null)))
            .IsFalse();
    }
}
