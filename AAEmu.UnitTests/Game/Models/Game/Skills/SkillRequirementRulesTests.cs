using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillRequirementRulesTests
{
    private static SkillRequirement ForbidTag(uint tagId, string message = "cannot while tagged") =>
        new(OnTarget: false, BuffId: 0, BuffTagId: tagId, Require: false, Message: message);

    private static SkillRequirement RequireTag(uint tagId, string message = "needs the tag") =>
        new(OnTarget: false, BuffId: 0, BuffTagId: tagId, Require: true, Message: message);

    private static bool Allows(IReadOnlyList<SkillRequirement> requirements, params (uint buff, uint tag)[] state)
    {
        var buffs = state.Select(entry => entry.buff).ToHashSet();
        var tags = state.Select(entry => entry.tag).ToHashSet();
        return SkillRequirementRules.AllowsCast(
            requirements,
            requirement => requirement.BuffId > 0 && buffs.Contains(requirement.BuffId),
            requirement => requirement.BuffTagId > 0 && tags.Contains(requirement.BuffTagId),
            out _);
    }

    [Test]
    public async Task ForbidRow_BlocksWhileTheUnitCarriesTheTag()
    {
        // Requirement 1: tag 27 발묶임 (rooted) — "cannot use this while bound".
        var requirements = new[] { ForbidTag(27) };

        await Assert.That(Allows(requirements, (0u, 0u))).IsTrue();
        await Assert.That(Allows(requirements, (0u, 27u))).IsFalse();
    }

    [Test]
    public async Task ForbidRow_ReportsItsClientMessage()
    {
        var requirements = new[] { ForbidTag(27, "발묶임 상태에서는 사용할 수 없습니다.") };

        await Assert.That(SkillRequirementRules.AllowsCast(
            requirements,
            _ => false,
            _ => true,
            out var message)).IsFalse();
        await Assert.That(message).IsEqualTo("발묶임 상태에서는 사용할 수 없습니다.");
    }

    [Test]
    public async Task RequireRow_BlocksUntilTheUnitCarriesTheTag()
    {
        // Requirement 15: tag 294 날틀 비행중 (gliding) — the glider skills.
        var requirements = new[] { RequireTag(294) };

        await Assert.That(Allows(requirements, (0u, 0u))).IsFalse();
        await Assert.That(Allows(requirements, (0u, 294u))).IsTrue();
    }

    [Test]
    public async Task RequirementNamingABuffId_UsesTheBuff()
    {
        // Requirement 21: buff 1729 화가 난 야생말 (angry wild horse) on the target.
        var requirements = new[] { new SkillRequirement(true, 1729, 0, false, "cannot use on that") };

        await Assert.That(Allows(requirements, (0u, 0u))).IsTrue();
        await Assert.That(Allows(requirements, (1729u, 0u))).IsFalse();
    }

    [Test]
    public async Task RequireRows_CombineWithOr()
    {
        // 강인한 의지 (11429) carries requirements 58 (restraint) and 59 (fear). Reading those as AND
        // would demand both states at once, so the cast could never happen.
        var requirements = new[] { RequireTag(4981), RequireTag(12) };

        await Assert.That(Allows(requirements, (0u, 0u))).IsFalse();
        await Assert.That(Allows(requirements, (0u, 4981u))).IsTrue();
        await Assert.That(Allows(requirements, (0u, 12u))).IsTrue();
    }

    [Test]
    public async Task ForbidRows_CombineWithAnd()
    {
        // Two forbids: either state blocks the cast on its own.
        var requirements = new[] { ForbidTag(6), ForbidTag(12) };

        await Assert.That(Allows(requirements, (0u, 6u))).IsFalse();
        await Assert.That(Allows(requirements, (0u, 12u))).IsFalse();
        await Assert.That(Allows(requirements, (0u, 0u))).IsTrue();
    }

    [Test]
    public async Task ForbidRowWins_WhenBothPolaritiesAreOnOneSkill()
    {
        // A forbid is checked whatever the require rows say.
        var requirements = new[] { ForbidTag(27), RequireTag(294) };

        await Assert.That(Allows(requirements, (0u, 294u))).IsTrue();
        await Assert.That(Allows(requirements, (0u, 27u))).IsFalse();
        await Assert.That(Allows(requirements, (0u, 27u | 294u))).IsFalse();
    }

    [Test]
    public async Task NoRequirements_AllowsTheCast()
    {
        await Assert.That(SkillRequirementRules.AllowsCast([], _ => false, _ => true, out _)).IsTrue();
        await Assert.That(SkillRequirementRules.AllowsCast(null, _ => false, _ => true, out _)).IsTrue();
    }
}
