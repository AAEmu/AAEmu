using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Expeditions;

public sealed class GuildBuffDurationRulesTests
{
    private static ExpeditionBuffGrade Grade(uint id = 70) => new() { Id = id, ExpeditionBuffId = 7, Grade = 1 };

    [Test]
    public async Task UnknownGradeIsTypedSeparately()
    {
        var decision = GuildBuffDurationRules.Classify(null, []);

        await Assert.That(decision).IsEqualTo(GuildBuffDurationSemantics.UnknownGrade);
    }

    [Test]
    public async Task NoDurationRowMeansNoAuthoredDurationModifier()
    {
        var modifiers = new[]
        {
            new BuffModifier { BuffAttribute = BuffAttribute.InDuration, UnitModifierType = UnitModifierType.Value, Value = 2 }
        };

        var decision = GuildBuffDurationRules.Classify(Grade(), modifiers);

        await Assert.That(decision).IsEqualTo(GuildBuffDurationSemantics.NotAuthored);
    }

    [Test]
    public async Task DurationRowIsClassifiedWithoutInventingAGradeExpiry()
    {
        var modifiers = new[]
        {
            new BuffModifier { BuffAttribute = BuffAttribute.Duration, UnitModifierType = UnitModifierType.Value, Value = -20 }
        };

        var decision = GuildBuffDurationRules.Classify(Grade(), modifiers);

        await Assert.That(decision).IsEqualTo(GuildBuffDurationSemantics.AppliedThroughBuffModifier);
    }
}
