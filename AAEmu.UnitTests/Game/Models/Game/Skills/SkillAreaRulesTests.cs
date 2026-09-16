using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillAreaRulesTests
{
    [Test]
    public async Task FullSweep_IsNotACone()
    {
        // 360 is the DB default on 36,679 skills and must stay a full circle.
        await Assert.That(SkillAreaRules.ConeHalfAngle(360, 0)).IsEqualTo(0d);
        // 365 is the same statement with slop on 26 rows.
        await Assert.That(SkillAreaRules.ConeHalfAngle(365, 0)).IsEqualTo(0d);
        await Assert.That(SkillAreaRules.ConeHalfAngle(0, 0)).IsEqualTo(0d);
    }

    [Test]
    public async Task AreaAngle_IsTheHalfAngle()
    {
        await Assert.That(SkillAreaRules.ConeHalfAngle(90, 0)).IsEqualTo(90d);
        await Assert.That(SkillAreaRules.ConeHalfAngle(180, 40)).IsEqualTo(180d);
        await Assert.That(SkillAreaRules.ConeHalfAngle(5, 0)).IsEqualTo(5d);
    }

    [Test]
    public async Task FrontAngle_OnlyAppliesWhenTheAreaAngleIsTheDefault()
    {
        await Assert.That(SkillAreaRules.ConeHalfAngle(360, 40)).IsEqualTo(40d);
        await Assert.That(SkillAreaRules.ConeHalfAngle(365, 180)).IsEqualTo(180d);
        await Assert.That(SkillAreaRules.ConeHalfAngle(360, 360)).IsEqualTo(0d);
        await Assert.That(SkillAreaRules.ConeHalfAngle(360, -90)).IsEqualTo(0d);
    }

    [Test]
    public async Task Cone_ExcludesWhatIsBehindTheCaster()
    {
        // A 90° cone: straight ahead is 0, behind is ±180.
        const double halfAngle = 90d;

        await Assert.That(SkillAreaRules.IsInsideCone(0, halfAngle)).IsTrue();
        await Assert.That(SkillAreaRules.IsInsideCone(89, halfAngle)).IsTrue();
        await Assert.That(SkillAreaRules.IsInsideCone(-89, halfAngle)).IsTrue();
        await Assert.That(SkillAreaRules.IsInsideCone(91, halfAngle)).IsFalse();
        await Assert.That(SkillAreaRules.IsInsideCone(-91, halfAngle)).IsFalse();
        // Directly behind the caster.
        await Assert.That(SkillAreaRules.IsInsideCone(180, halfAngle)).IsFalse();
    }

    [Test]
    public async Task Cone_NoConeAcceptsEveryBearing()
    {
        await Assert.That(SkillAreaRules.IsInsideCone(180, 0)).IsTrue();
        await Assert.That(SkillAreaRules.IsInsideCone(-179, 0)).IsTrue();
    }

    [Test]
    public async Task Corridor_KeepsWhatIsBetweenCasterAndTarget()
    {
        // Caster at the origin, target 10m along +X, corridor half-width 2m.
        (float, float) caster = (0f, 0f);
        (float, float) target = (10f, 0f);

        await Assert.That(SkillAreaRules.IsWithinCorridor(caster, target, (5f, 1.5f), 2d)).IsTrue();
        await Assert.That(SkillAreaRules.IsWithinCorridor(caster, target, (5f, 2.5f), 2d)).IsFalse();
        // The corridor ends in a cap around the target, so a unit beside the target still qualifies...
        await Assert.That(SkillAreaRules.IsWithinCorridor(caster, target, (11f, 0f), 2d)).IsTrue();
        // ...but not one past that cap.
        await Assert.That(SkillAreaRules.IsWithinCorridor(caster, target, (13f, 0f), 2d)).IsFalse();
        // The near end has a cap too, around the caster; beyond it the line never started.
        await Assert.That(SkillAreaRules.IsWithinCorridor(caster, target, (-1f, 0f), 2d)).IsTrue();
        await Assert.That(SkillAreaRules.IsWithinCorridor(caster, target, (-3f, 0f), 2d)).IsFalse();
    }

    [Test]
    public async Task Corridor_WithNoWidthOrNoLine_ReachesNothing()
    {
        await Assert.That(SkillAreaRules.IsWithinCorridor((0f, 0f), (10f, 0f), (5f, 0f), 0d)).IsFalse();
        // A cast on the caster's own position has no line.
        await Assert.That(SkillAreaRules.IsWithinCorridor((0f, 0f), (0f, 0f), (0f, 0f), 2d)).IsFalse();
    }

    [Test]
    public async Task Selection_LocationGathersAroundTheCastPosition_AndLineUsesTheCorridor()
    {
        await Assert.That(SkillAreaRules.GathersAroundCastPosition(SkillTargetSelection.Location)).IsTrue();
        await Assert.That(SkillAreaRules.GathersAroundCastPosition(SkillTargetSelection.Source)).IsFalse();
        await Assert.That(SkillAreaRules.GathersAroundCastPosition(SkillTargetSelection.Target)).IsFalse();

        await Assert.That(SkillAreaRules.UsesCorridor(SkillTargetSelection.Line)).IsTrue();
        await Assert.That(SkillAreaRules.UsesCorridor(SkillTargetSelection.Target)).IsFalse();
        await Assert.That(SkillAreaRules.UsesCorridor(SkillTargetSelection.Source)).IsFalse();
        await Assert.That(SkillAreaRules.UsesCorridor(SkillTargetSelection.Location)).IsFalse();
    }
}
