using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillRequirementRulesTests
{
    private static SkillRequirement ForbidTag(uint tagId, string message = "cannot while tagged", uint id = 1) =>
        new(Id: id, OnTarget: false, BuffId: 0, BuffTagId: tagId, Require: false, Message: message);

    private static SkillRequirement RequireTag(uint tagId, string message = "needs the tag", uint id = 58) =>
        new(Id: id, OnTarget: false, BuffId: 0, BuffTagId: tagId, Require: true, Message: message);

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
    public async Task ForbidRow_ReportsItsClientMessageAndRowId()
    {
        var requirements = new[]
        {
            ForbidTag(27, "발묶임 상태에서는 사용할 수 없습니다.", id: 1)
        };

        await Assert.That(SkillRequirementRules.AllowsCast(
            requirements,
            _ => false,
            _ => true,
            out var message,
            out var failedId)).IsFalse();
        await Assert.That(message).IsEqualTo("발묶임 상태에서는 사용할 수 없습니다.");
        await Assert.That(failedId).IsEqualTo(1u);
    }

    [Test]
    public async Task PeaceZoneForbid_IsIgnoredWhileBuffIsUp()
    {
        // Dash / instruments / potions: client leaves them undimmed at Nuia; row 25 as a forbid must not block.
        var requirements = new[]
        {
            new SkillRequirement(
                Id: SkillRequirementRules.NuiaPeaceRequirementId,
                OnTarget: false,
                BuffId: 2149,
                BuffTagId: 0,
                Require: false,
                Message: "누이 여신 주변은 평화 지역 입니다")
        };

        await Assert.That(SkillRequirementRules.AllowsCast(
            requirements,
            r => r.BuffId == 2149,
            _ => false,
            out var message,
            out var failedId)).IsTrue();
        await Assert.That(failedId).IsEqualTo(0u);
        await Assert.That(message).IsNull();
    }

    [Test]
    public async Task OtherPeaceZoneForbids_AreAlsoIgnored()
    {
        foreach (var rowId in SkillRequirementRules.PeaceZoneNoticeRequirementIds)
        {
            var requirements = new[]
            {
                new SkillRequirement(
                    Id: rowId,
                    OnTarget: false,
                    BuffId: 1,
                    BuffTagId: 0,
                    Require: false,
                    Message: "peace zone")
            };
            await Assert.That(SkillRequirementRules.AllowsCast(
                requirements, _ => true, _ => false, out _, out _)).IsTrue();
        }
    }

    [Test]
    public async Task RequireRow_BlocksUntilTheUnitCarriesTheTag()
    {
        // Requirement 58: tag 4981 구속 (restraint) — 자유 (Freedom) is castable only while restrained.
        var requirements = new[] { RequireTag(4981) };

        await Assert.That(Allows(requirements, (0u, 0u))).IsFalse();
        await Assert.That(Allows(requirements, (0u, 4981u))).IsTrue();
    }

    [Test]
    public async Task ForbiddingRows_AreEveryRowButTheFourThatRequireTheirState()
    {
        // The mount summon (32211) carries requirement 25, the Nui peace zone. Reading 'f' as "must
        // carry" demands a peace buff and blocks every mount in the game; the row refuses the cast.
        await Assert.That(SkillRequirementRules.IsRowForbidding(25, false)).IsTrue();
        await Assert.That(SkillRequirementRules.IsRowForbidding(192, false)).IsTrue(); // courtroom
        await Assert.That(SkillRequirementRules.IsRowForbidding(402, false)).IsTrue(); // battlefield wait

        // 105 (landing) also rides on the mount summon, so it must forbid rather than require landing.
        await Assert.That(SkillRequirementRules.IsRowForbidding(105, false)).IsTrue();

        // 't' rows are forbids either way, e.g. requirement 1 발묶임 (rooted).
        await Assert.That(SkillRequirementRules.IsRowForbidding(1, true)).IsTrue();
    }

    [Test]
    public async Task RequireRows_AreTheStatesTheirSkillExistsIn()
    {
        // 15 gliding, 37 downed mount, 58 restraint, 59 fear.
        foreach (var rowId in new uint[] { 15, 37, 58, 59 })
            await Assert.That(SkillRequirementRules.IsRowForbidding(rowId, false)).IsFalse();

        await Assert.That(SkillRequirementRules.RequireRowsByDesign.Count).IsEqualTo(4);
    }

    [Test]
    public async Task ExpeditionPortalSave_FlipsNuiaPeaceRowToRequire()
    {
        var forbid = new SkillRequirement(
            Id: SkillRequirementRules.NuiaPeaceRequirementId,
            OnTarget: false,
            BuffId: 2149,
            BuffTagId: 0,
            Require: false,
            Message: "누이 여신 주변은 평화 지역 입니다");

        var forInk = SkillRequirementRules.WithDirectLinkPolarity(
            SkillRequirementRules.NuiaPeaceRequirementId, forbid, isExpeditionPortalSaveSkill: true);
        await Assert.That(forInk.Require).IsTrue();

        // Non-portal skills keep forbid polarity, but AllowsCast ignores that forbid.
        var forDash = SkillRequirementRules.WithDirectLinkPolarity(
            SkillRequirementRules.NuiaPeaceRequirementId, forbid, isExpeditionPortalSaveSkill: false);
        await Assert.That(forDash.Require).IsFalse();
        await Assert.That(SkillRequirementRules.IsIgnoredPeaceZoneForbid(forDash)).IsTrue();
    }

    [Test]
    public async Task ExpeditionPortalSave_RequireNuiaBuff_BlocksAwayFromStatue()
    {
        var requirements = new[]
        {
            SkillRequirementRules.WithDirectLinkPolarity(
                SkillRequirementRules.NuiaPeaceRequirementId,
                new SkillRequirement(
                    Id: 25,
                    OnTarget: false,
                    BuffId: 2149,
                    BuffTagId: 0,
                    Require: false,
                    Message: "누이 여신 주변은 평화 지역 입니다"),
                isExpeditionPortalSaveSkill: true)
        };

        await Assert.That(SkillRequirementRules.AllowsCast(
            requirements,
            _ => false,
            _ => false,
            out _,
            out var failedId)).IsFalse();
        await Assert.That(failedId).IsEqualTo(25u);

        await Assert.That(SkillRequirementRules.AllowsCast(
            requirements,
            r => r.BuffId == 2149,
            _ => false,
            out _,
            out _)).IsTrue();
    }

    [Test]
    public async Task RequirementNamingABuffId_UsesTheBuff()
    {
        // Requirement 21: buff 1729 화가 난 야생말 (angry wild horse) on the target.
        var requirements = new[]
        {
            new SkillRequirement(Id: 21, OnTarget: true, BuffId: 1729, BuffTagId: 0, Require: false,
                Message: "cannot use on that")
        };

        await Assert.That(Allows(requirements, (0u, 0u))).IsTrue();
        await Assert.That(Allows(requirements, (1729u, 0u))).IsFalse();
    }

    [Test]
    public async Task RequireRows_CombineWithOr()
    {
        // 강인한 의지 (11429) carries requirements 58 (restraint) and 59 (fear). Reading those as AND
        // would demand both states at once, so the cast could never happen.
        var requirements = new[] { RequireTag(4981, id: 58), RequireTag(12, id: 59) };

        await Assert.That(Allows(requirements, (0u, 0u))).IsFalse();
        await Assert.That(Allows(requirements, (0u, 4981u))).IsTrue();
        await Assert.That(Allows(requirements, (0u, 12u))).IsTrue();
    }

    [Test]
    public async Task ForbidRows_CombineWithAnd()
    {
        // Two forbids: either state blocks the cast on its own.
        var requirements = new[] { ForbidTag(6, id: 10), ForbidTag(12, id: 11) };

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
