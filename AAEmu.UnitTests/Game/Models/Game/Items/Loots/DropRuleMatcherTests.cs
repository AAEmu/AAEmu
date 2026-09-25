using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.UnitTests.Game.Models.Game.Items.Loots;

[NotInParallel]
public sealed class DropRuleMatcherTests
{
    [Test]
    public async Task NumericBoundariesAndBooleanCompositionAreDeterministic()
    {
        var valid = Parse("level >= 10 AND level < 20 AND npc_tendency_id IN (2, 9, 10) AND NOT (npc_grade_id = 3)");

        await Assert.That(valid.IsValid).IsTrue();
        await Assert.That(valid.Matches(Subject(level: 10, tendency: 9, grade: 2))).IsTrue();
        await Assert.That(valid.Matches(Subject(level: 19, tendency: 2, grade: 1))).IsTrue();
        await Assert.That(valid.Matches(Subject(level: 9, tendency: 2, grade: 1))).IsFalse();
        await Assert.That(valid.Matches(Subject(level: 10, tendency: 2, grade: 3))).IsFalse();
        await Assert.That(valid.Matches(Subject(level: 10, tendency: 3, grade: 1))).IsFalse();
    }

    [Test]
    public async Task LikeAndNegationUseTheContentFieldsWithoutNameClassification()
    {
        var valid = Parse("name LIKE '%wolf%' AND comment2 NOT LIKE '%event%' AND aggression = 't'");
        var subject = Subject(name: "Winter Wolf", comment2: "field", aggression: true);

        await Assert.That(valid.IsValid).IsTrue();
        await Assert.That(valid.Matches(subject)).IsTrue();
        await Assert.That(valid.Matches(subject with { Name = "Winter Bear" })).IsFalse();
        await Assert.That(valid.Matches(subject with { Comment2 = "event field" })).IsFalse();
        await Assert.That(valid.Matches(subject with { Aggression = false })).IsFalse();
    }

    [Test]
    public async Task NullValuesDoNotSatisfyNegatedSqlPredicates()
    {
        var notLike = Parse("NOT (comment2 LIKE '%event%')");
        var notIn = Parse("level NOT IN (1, 2)");
        var subject = Subject(level: null, comment2: null);

        await Assert.That(notLike.Matches(subject)).IsFalse();
        await Assert.That(notIn.Matches(subject)).IsFalse();
    }

    [Test]
    public async Task UnsupportedMatcherSyntaxFailsClosed()
    {
        var between = DropRuleMatcher.TryParse("level BETWEEN 10 AND 20", out var betweenMatcher, out _);
        var unsupportedField = DropRuleMatcher.TryParse("unknown_field = 1", out var fieldMatcher, out _);

        await Assert.That(between).IsFalse();
        await Assert.That(betweenMatcher is null || !betweenMatcher.IsValid).IsTrue();
        await Assert.That(unsupportedField).IsFalse();
        await Assert.That(fieldMatcher is null || !fieldMatcher.IsValid).IsTrue();
    }

    private static DropRuleMatcher Parse(string expression)
    {
        if (!DropRuleMatcher.TryParse(expression, out var matcher, out var error) || matcher is null)
            throw new InvalidOperationException(error);
        return matcher;
    }

    private static DropRuleSubject Subject(
        int? level = 1,
        int? tendency = 2,
        int? grade = 1,
        string? name = "",
        string? comment2 = "",
        bool? aggression = false) => new(
            1,
            level,
            tendency,
            grade,
            1,
            0,
            0,
            name,
            string.Empty,
            comment2,
            string.Empty,
            aggression);
}
