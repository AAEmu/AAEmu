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

    [Test]
    public async Task SoldierRankWithoutDifficultyEvidence_IsExplicitlyRejected()
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
