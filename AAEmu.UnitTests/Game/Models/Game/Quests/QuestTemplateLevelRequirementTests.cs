using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

/// <summary>
/// The level gate of a quest template, which a refusal now names on the client's own level row
/// instead of the generic requirement row.
/// </summary>
public class QuestTemplateLevelRequirementTests
{
    [Test]
    public async Task BelowTheMinimumLevel_IsRefused()
    {
        var character = CreateCharacter(level: 5);
        var template = new QuestTemplate { Id = 1, MinLevel = 10, RaceMask = byte.MaxValue };

        await Assert.That(template.MeetsLevelRequirements(character)).IsFalse();
        await Assert.That(template.MeetsContextRequirements(character)).IsFalse();
    }

    [Test]
    public async Task AboveACappedMaximumLevel_IsRefused()
    {
        var character = CreateCharacter(level: 40);
        var template = new QuestTemplate { Id = 1, MinLevel = 10, MaxLevel = 30, RaceMask = byte.MaxValue };

        await Assert.That(template.MeetsLevelRequirements(character)).IsFalse();
        await Assert.That(template.MeetsContextRequirements(character)).IsFalse();
    }

    [Test]
    public async Task WithoutAMaximumLevel_OnlyTheMinimumIsChecked()
    {
        var character = CreateCharacter(level: 60);
        var template = new QuestTemplate { Id = 1, MinLevel = 10, RaceMask = byte.MaxValue };

        await Assert.That(template.MeetsLevelRequirements(character)).IsTrue();
        await Assert.That(template.MeetsContextRequirements(character)).IsTrue();
    }

    [Test]
    public async Task RaceTheMaskExcludes_StillFailsTheContextCheckWhileTheLevelGatePasses()
    {
        // A race refusal must keep the generic requirement row, so the level gate has to stay true.
        var character = CreateCharacter(level: 20);
        character.Race = Race.Elf;
        var template = new QuestTemplate
        {
            Id = 1,
            MinLevel = 10,
            RaceMask = (byte)(1 << ((int)Race.Nuian - 1))
        };

        await Assert.That(template.MeetsLevelRequirements(character)).IsTrue();
        await Assert.That(template.MeetsContextRequirements(character)).IsFalse();
    }

    [Test]
    public async Task LevelGate_HasItsOwnClientRow()
    {
        await Assert.That(QuestAcceptFailRules.LevelNotMet).IsEqualTo(QuestStatusFailed.LevelNotMatch);
        await Assert.That((byte)QuestAcceptFailRules.LevelNotMet).IsEqualTo((byte)25);
    }

    private static Character CreateCharacter(byte level) =>
        new(new UnitCustomModelParams()) { Id = 42, Name = "Tester", Level = level, Race = Race.Nuian };
}
