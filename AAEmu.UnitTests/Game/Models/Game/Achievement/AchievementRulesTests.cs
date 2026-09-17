using AAEmu.Game.Models.Game.Achievement;

namespace AAEmu.UnitTests.Game.Models.Game.Achievement;

/// <summary>
/// The evaluation reading, pinned against the shapes the shipped content actually uses.
/// </summary>
public class AchievementRulesTests
{
    private static AchievementObjective[] Objectives(params uint[] recordIds) =>
        [.. recordIds.Select((recordId, index) => new AchievementObjective((uint)(index + 1), recordId))];

    private static Func<uint, int> Records(params (uint RecordId, int Value)[] records)
    {
        var map = records.ToDictionary(r => r.RecordId, r => r.Value);
        return recordId => map.GetValueOrDefault(recordId);
    }

    [Test]
    public async Task Summing_shape_adds_the_records_up_to_complete_num()
    {
        // "Complete 1,200 quests" over eleven records: the number is the total, not a count of objectives.
        var objectives = Objectives([.. Enumerable.Range(1, 11).Select(i => (uint)i)]);

        var partial = AchievementRules.Evaluate(1200, false, objectives, Records((1, 700), (2, 499)));
        await Assert.That(partial.Progress).IsEqualTo(1199);
        await Assert.That(partial.Required).IsEqualTo(1200);
        await Assert.That(partial.Complete).IsFalse();

        var done = AchievementRules.Evaluate(1200, false, objectives, Records((1, 700), (2, 500)));
        await Assert.That(done.Progress).IsEqualTo(1200);
        await Assert.That(done.Complete).IsTrue();
    }

    [Test]
    public async Task Summing_shape_can_ask_for_more_than_it_has_objectives()
    {
        // 361 rows do this: 10,000 kills watched by nine records, 50 ability levels by one.
        var one = Objectives(1);
        await Assert.That(AchievementRules.Evaluate(50, false, one, Records((1, 49))).Complete).IsFalse();
        await Assert.That(AchievementRules.Evaluate(50, false, one, Records((1, 50))).Complete).IsTrue();
    }

    [Test]
    public async Task Summing_shape_treats_its_objectives_as_alternatives()
    {
        // 168 candidate armor pieces behind "obtain an Eferium armor": any single one is enough.
        var objectives = Objectives([.. Enumerable.Range(1, 168).Select(i => (uint)i)]);
        var evaluation = AchievementRules.Evaluate(1, false, objectives, Records((168, 1)));

        await Assert.That(evaluation.Satisfied).IsEqualTo(1);
        await Assert.That(evaluation.Progress).IsEqualTo(1);
        await Assert.That(evaluation.Complete).IsTrue();
    }

    [Test]
    public async Task Counting_shape_needs_that_many_objectives()
    {
        // The novel-crafting ladder: 45 titles, asking for 5, then 10, then 20, then all 45 kinds.
        var objectives = Objectives([.. Enumerable.Range(1, 45).Select(i => (uint)i)]);
        var five = Records([.. Enumerable.Range(1, 5).Select(i => ((uint)i, 1))]);

        await Assert.That(AchievementRules.Evaluate(5, true, objectives, five).Complete).IsTrue();
        await Assert.That(AchievementRules.Evaluate(10, true, objectives, five).Progress).IsEqualTo(5);
        await Assert.That(AchievementRules.Evaluate(10, true, objectives, five).Complete).IsFalse();
        await Assert.That(AchievementRules.Evaluate(45, true, objectives, five).Complete).IsFalse();

        // A record far past its own target is still one kind.
        var heavy = Records((1, 999_999), (2, 999_999), (3, 999_999), (4, 999_999), (5, 999_999));
        await Assert.That(AchievementRules.Evaluate(5, true, objectives, heavy).Complete).IsTrue();
        await Assert.That(AchievementRules.Evaluate(10, true, objectives, heavy).Progress).IsEqualTo(5);
    }

    [Test]
    public async Task Counting_shape_completes_on_one_of_many()
    {
        // "Complete one of these 116 housing designs": the smallest tier of the counting shape.
        var objectives = Objectives([.. Enumerable.Range(1, 116).Select(i => (uint)i)]);
        var evaluation = AchievementRules.Evaluate(1, true, objectives, Records((77, 1)));

        await Assert.That(evaluation.Progress).IsEqualTo(1);
        await Assert.That(evaluation.Complete).IsTrue();
    }

    [Test]
    public async Task Complete_num_zero_means_all_of_them_one_each()
    {
        // "Train the abilities of fourteen heroes ... for all fourteen".
        var objectives = Objectives([.. Enumerable.Range(1, 14).Select(i => (uint)i)]);

        var thirteen = Records([.. Enumerable.Range(1, 13).Select(i => ((uint)i, 50))]);
        var partial = AchievementRules.Evaluate(0, true, objectives, thirteen);
        await Assert.That(partial.Required).IsEqualTo(14);
        await Assert.That(partial.Satisfied).IsEqualTo(13);
        await Assert.That(partial.Complete).IsFalse();

        var fourteen = Records([.. Enumerable.Range(1, 14).Select(i => ((uint)i, 1))]);
        var done = AchievementRules.Evaluate(0, true, objectives, fourteen);
        await Assert.That(done.Progress).IsEqualTo(14);
        await Assert.That(done.Complete).IsTrue();

        // The same under the summing flag, where a record's size would otherwise carry the total.
        await Assert.That(AchievementRules.Evaluate(0, false, objectives, thirteen).Complete).IsFalse();
        await Assert.That(AchievementRules.Evaluate(0, false, objectives, fourteen).Complete).IsTrue();
    }

    [Test]
    public async Task Reported_progress_never_exceeds_what_the_achievement_asks_for()
    {
        // The amount is sent to the client and draws its bar, so a maxed-out record is clamped.
        var objectives = Objectives(1, 2);
        var evaluation = AchievementRules.Evaluate(1000, false, objectives, Records((1, 50_000), (2, 50_000)));

        await Assert.That(evaluation.Progress).IsEqualTo(1000);
        await Assert.That(evaluation.Complete).IsTrue();
    }

    [Test]
    public async Task Records_at_or_below_zero_do_not_count()
    {
        // The table stores -1 as its "no target" sentinel, and untouched counters read 0.
        var objectives = Objectives(1, 2, 3);
        var evaluation = AchievementRules.Evaluate(0, true, objectives, Records((1, 0), (2, -1), (3, 1)));

        await Assert.That(evaluation.Satisfied).IsEqualTo(1);
        await Assert.That(evaluation.Progress).IsEqualTo(1);
        await Assert.That(evaluation.Complete).IsFalse();
    }

    [Test]
    public async Task Duplicate_objectives_each_count()
    {
        // The content lists the same record more than once, and each row is its own objective.
        var objectives = Objectives(7, 7);
        var evaluation = AchievementRules.Evaluate(0, true, objectives, Records((7, 3)));

        await Assert.That(evaluation.Required).IsEqualTo(2);
        await Assert.That(evaluation.Progress).IsEqualTo(2);
        await Assert.That(evaluation.Complete).IsTrue();
    }

    [Test]
    public async Task An_achievement_without_objectives_is_never_completed()
    {
        // 78 achievements have no objectives; nothing counts towards them, so nothing completes them.
        var evaluation = AchievementRules.Evaluate(0, true, [], Records());

        await Assert.That(evaluation.Required).IsEqualTo(0);
        await Assert.That(evaluation.Progress).IsEqualTo(0);
        await Assert.That(evaluation.Complete).IsFalse();
    }

    [Test]
    public async Task Missing_input_arguments_are_tolerated()
    {
        var evaluation = AchievementRules.Evaluate(10, false, null, null);

        await Assert.That(evaluation.Progress).IsEqualTo(0);
        await Assert.That(evaluation.Complete).IsFalse();
    }
}
