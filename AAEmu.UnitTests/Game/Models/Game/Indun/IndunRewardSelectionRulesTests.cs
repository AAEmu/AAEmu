using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

public class IndunRewardSelectionRulesTests
{
    private static InstanceReward Reward(uint id, uint kind, int start, int end, uint target = 100) =>
        new(id, 10, kind, start, end, 1, false, target, InstanceRewardTargetType.Item, false, false);

    [Test]
    public async Task DifficultySelection_RequiresBothEvidenceAndAValue()
    {
        await Assert.That(IndunRewardSelectionRules.TryResolveDifficultySelection(true, 3, out var selected)).IsTrue();
        await Assert.That(selected).IsEqualTo(3);
        await Assert.That(IndunRewardSelectionRules.TryResolveDifficultySelection(false, 3, out _)).IsFalse();
        await Assert.That(IndunRewardSelectionRules.TryResolveDifficultySelection(true, null, out _)).IsFalse();
    }

    [Test]
    public async Task AuthoredDifficultySelection_RejectsUncoveredValues()
    {
        var rewards = new[] { Reward(1, 6, 1, 2) };
        await Assert.That(IndunRewardSelectionRules.TryResolveAuthoredDifficultySelection(rewards, true, 2, 6, out var selected)).IsTrue();
        await Assert.That(selected).IsEqualTo(2);
        await Assert.That(IndunRewardSelectionRules.TryResolveAuthoredDifficultySelection(rewards, true, 3, 6, out _)).IsFalse();
        await Assert.That(IndunRewardSelectionRules.TryResolveAuthoredDifficultySelection(rewards, false, 2, 6, out _)).IsFalse();
    }

    /// <summary>
    /// The difficulty path cannot resolve a value when the instance publishes no difficulty info. This
    /// is a property of the difficulty source only — it is not a claim that a rank-backed kind is
    /// unauthored. Rank bands are authored; see <c>InstanceRewardTaxonomyRulesTests</c>.
    /// </summary>
    [Test]
    public async Task DifficultySource_RejectsAnInstanceThatPublishesNoDifficultyInfo()
    {
        await Assert.That(IndunRewardSelectionRules.TryResolveDifficultySelection(false, 1, out _)).IsFalse();
        await Assert.That(IndunRewardSelectionRules.TryResolveDifficultySelection(false, 4, out _)).IsFalse();
    }

    [Test]
    public async Task Select_ReturnsEveryOverlappingRow()
    {
        var rewards = new[]
        {
            Reward(1, 6, 1, 4, 101),
            Reward(2, 6, 3, 6, 102),
            Reward(3, 7, 1, 4, 103)
        };

        var selected = IndunRewardSelectionRules.Select(rewards, 6, 3);

        await Assert.That(selected.Count).IsEqualTo(2);
        await Assert.That(selected[0].Id).IsEqualTo(1u);
        await Assert.That(selected[1].Id).IsEqualTo(2u);
    }

    [Test]
    public async Task Select_ExcludesOtherKinds()
    {
        var rewards = new[] { Reward(1, 6, 1, 2), Reward(2, 7, 1, 2) };

        var selected = IndunRewardSelectionRules.Select(rewards, 7, 1);

        await Assert.That(selected.Count).IsEqualTo(1);
        await Assert.That(selected[0].InstanceRewardKindId).IsEqualTo(7u);
    }

    [Test]
    public async Task Select_FailsLoudlyWhenNoRangeMatches()
    {
        var rewards = new[] { Reward(1, 6, 1, 2) };

        await Assert.That(() => IndunRewardSelectionRules.Select(rewards, 6, 3))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task SelectBonusCounts_KeepsOnlySelectedRewardsInTypedOrder()
    {
        var selected = new[] { Reward(2, 6, 1, 2), Reward(1, 6, 1, 2) };
        var bonuses = new[]
        {
            new InstanceRewardBonusCount(3, 1, 200, 2),
            new InstanceRewardBonusCount(1, 2, 100, 1),
            new InstanceRewardBonusCount(2, 1, 100, 1)
        };

        var result = IndunRewardSelectionRules.SelectBonusCounts(selected, bonuses);

        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result[0].InstanceRewardId).IsEqualTo(1u);
        await Assert.That(result[0].BuffId).IsEqualTo(100u);
        await Assert.That(result[1].BuffId).IsEqualTo(200u);
        await Assert.That(result[2].InstanceRewardId).IsEqualTo(2u);
    }

    [Test]
    public async Task SelectBonusCounts_RejectsOrphanDuplicateAndNonPositiveRows()
    {
        var selected = new[] { Reward(1, 6, 1, 2) };

        await Assert.That(() => IndunRewardSelectionRules.SelectBonusCounts(selected,
                [new InstanceRewardBonusCount(1, 2, 100, 1)]))
            .Throws<InvalidDataException>();
        await Assert.That(() => IndunRewardSelectionRules.SelectBonusCounts(selected,
                [
                    new InstanceRewardBonusCount(1, 1, 100, 1),
                    new InstanceRewardBonusCount(2, 1, 100, 2)
                ]))
            .Throws<InvalidDataException>();
        await Assert.That(() => IndunRewardSelectionRules.SelectBonusCounts(selected,
                [new InstanceRewardBonusCount(1, 1, 100, 0)]))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task MailKind_MapsByTypedCatalogNameAndFailsLoudlyOtherwise()
    {
        await Assert.That(IndunRewardMailKindRules.Map(InstanceRewardMailKind.Basic))
            .IsEqualTo(AAEmu.Game.Models.Game.Mails.MailType.SysExpress);
        await Assert.That(() => IndunRewardMailKindRules.Map((InstanceRewardMailKind)999))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task Select_DoesNotInventARewardForAnUnknownKind()
    {
        var rewards = new[] { Reward(1, 6, 1, 2) };

        await Assert.That(() => IndunRewardSelectionRules.Select(rewards, 99, 1))
            .Throws<InvalidDataException>();
    }
}
