using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class DefaultSkillAssignRulesTests
{
    // Compact character_default_skills: Nuian 35420/35418, Hariharan 35423/35424.
    private static readonly HashSet<uint> RaceAssigned =
    [
        35418, 35420, 35421, 35422, 35423, 35424, 35425, 35426, 35427, 35428, 33984, 33985
    ];

    private static readonly HashSet<uint> Nuian = [35420, 35418];
    private static readonly HashSet<uint> Hariharan = [35423, 35424];

    [Test]
    public async Task SharedSkill_AppliesToEveryRace()
    {
        await Assert.That(DefaultSkillAssignRules.AppliesToCharacter(2, RaceAssigned, Nuian)).IsTrue();
        await Assert.That(DefaultSkillAssignRules.AppliesToCharacter(2, RaceAssigned, Hariharan)).IsTrue();
        await Assert.That(DefaultSkillAssignRules.AppliesToCharacter(2, RaceAssigned, new HashSet<uint>())).IsTrue();
    }

    [Test]
    public async Task RacialSkill_StaysOnItsOwnRace()
    {
        await Assert.That(DefaultSkillAssignRules.AppliesToCharacter(35420, RaceAssigned, Nuian)).IsTrue();
        await Assert.That(DefaultSkillAssignRules.AppliesToCharacter(35418, RaceAssigned, Nuian)).IsTrue();
        await Assert.That(DefaultSkillAssignRules.AppliesToCharacter(35423, RaceAssigned, Nuian)).IsFalse();
        await Assert.That(DefaultSkillAssignRules.AppliesToCharacter(35420, RaceAssigned, Hariharan)).IsFalse();
        await Assert.That(DefaultSkillAssignRules.AppliesToCharacter(35423, RaceAssigned, Hariharan)).IsTrue();
    }

    [Test]
    public async Task RaceWithoutTemplateRows_KeepsOnlySharedSkills()
    {
        // Fairy / Returned have no character_default_skills rows in compact.
        await Assert.That(DefaultSkillAssignRules.AppliesToCharacter(2, RaceAssigned, null)).IsTrue();
        await Assert.That(DefaultSkillAssignRules.AppliesToCharacter(35420, RaceAssigned, null)).IsFalse();
        await Assert.That(DefaultSkillAssignRules.AppliesToCharacter(35420, RaceAssigned, new HashSet<uint>())).IsFalse();
    }

    [Test]
    public async Task UnlistedDuplicateRow_DoesNotMakeARacialSkillUniversal()
    {
        // default_skills id 157 is 33984 with no character_default_skills row;
        // id 156 is the same skill on Warborn. Assignment is by skill id.
        var warborn = new HashSet<uint> { 33984, 35428 };
        await Assert.That(DefaultSkillAssignRules.AppliesToCharacter(33984, RaceAssigned, warborn)).IsTrue();
        await Assert.That(DefaultSkillAssignRules.AppliesToCharacter(33984, RaceAssigned, Nuian)).IsFalse();
    }
}
