using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.UnitTests.Game.Models.Game.Indun;

/// <summary>
/// W03C selection-taxonomy tests. Every case is pure and deterministic: no clock, no I/O, no timing.
/// The values used here are synthetic shapes, not shipped content ids, so the tests describe the
/// structural rule rather than a specific row set.
/// </summary>
public class InstanceRewardTaxonomyRulesTests
{
    private const uint InstanceId = 900;
    private const uint Kind = 91;

    private static InstanceReward Reward(uint id, int start, int end, uint kind = Kind) =>
        new(id, InstanceId, kind, start, end, 1, false, 100, InstanceRewardTargetType.Item, false, false);

    private static InstanceRewardSelectionVerdict Classify(
        IReadOnlyList<InstanceReward> rewards,
        IReadOnlyCollection<byte> difficulties = null,
        int roundCount = 0,
        IReadOnlyCollection<int> teamSizes = null,
        bool displaySurface = false,
        bool trigger = true,
        byte? runtimeDifficulty = null,
        uint kind = Kind,
        string kindName = "synthetic_kind") =>
        InstanceRewardTaxonomyRules.Classify(
            InstanceId, kind, kindName, rewards,
            difficulties ?? [], roundCount, teamSizes ?? [], displaySurface, trigger, runtimeDifficulty);

    [Test]
    public async Task DifficultyBacked_IsDeliverableWhenATriggerIsAuthored()
    {
        var rewards = new[] { Reward(1, 1, 12) };
        var difficulties = new byte[] { 1, 2, 3 };

        var verdict = Classify(rewards, difficulties, runtimeDifficulty: 3);

        await Assert.That(verdict.Classification).IsEqualTo(InstanceRewardSelectionClass.DifficultyBacked);
        await Assert.That(verdict.SelectionSourceProven).IsTrue();
        await Assert.That(verdict.DeliverableNow).IsTrue();
        await Assert.That(verdict.Blocker).IsEqualTo(InstanceRewardBlocker.None);
        await Assert.That(verdict.SelectionValue).IsEqualTo(3);
    }

    [Test]
    public async Task DifficultyBacked_FallsBackToTheLowestAuthoredDifficultyBeforeSelection()
    {
        var rewards = new[] { Reward(1, 1, 12) };
        var difficulties = new byte[] { 5, 7, 9 };

        var verdict = Classify(rewards, difficulties);

        await Assert.That(verdict.Classification).IsEqualTo(InstanceRewardSelectionClass.DifficultyBacked);
        await Assert.That(verdict.SelectionValue).IsEqualTo(5);
    }

    [Test]
    public async Task DifficultyBacked_IgnoresARuntimeDifficultyOutsideEveryAuthoredRange()
    {
        var rewards = new[] { Reward(1, 1, 3) };
        var difficulties = new byte[] { 1, 2, 3 };

        var verdict = Classify(rewards, difficulties, runtimeDifficulty: 99);

        await Assert.That(verdict.Classification).IsEqualTo(InstanceRewardSelectionClass.DifficultyBacked);
        await Assert.That(verdict.SelectionValue).IsEqualTo(1);
    }

    [Test]
    public async Task DifficultyBacked_WithoutATriggerNamesTheMissingTrigger()
    {
        var rewards = new[] { Reward(1, 1, 12) };

        var verdict = Classify(rewards, [1, 2, 3], trigger: false);

        await Assert.That(verdict.Classification).IsEqualTo(InstanceRewardSelectionClass.DifficultyBacked);
        await Assert.That(verdict.SelectionSourceProven).IsTrue();
        await Assert.That(verdict.DeliverableNow).IsFalse();
        await Assert.That(verdict.Blocker).IsEqualTo(InstanceRewardBlocker.MissingDeliveryTrigger);
    }

    [Test]
    public async Task RoundBacked_ProvenOnlyByAnExactRoundCountCover()
    {
        var rewards = new[] { Reward(1, 1, 4), Reward(2, 5, 9) };

        var verdict = Classify(rewards, roundCount: 9);

        await Assert.That(verdict.Classification).IsEqualTo(InstanceRewardSelectionClass.RoundBacked);
        await Assert.That(verdict.SelectionSourceProven).IsTrue();
        await Assert.That(verdict.SelectionValue).IsEqualTo(9);
    }

    [Test]
    public async Task RoundBacked_RejectsACoverThatDoesNotReachTheRoundCount()
    {
        var rewards = new[] { Reward(1, 1, 4) };

        var verdict = Classify(rewards, roundCount: 9);

        await Assert.That(verdict.Classification).IsNotEqualTo(InstanceRewardSelectionClass.RoundBacked);
    }

    [Test]
    public async Task RoundBacked_RejectsRangesThatDoNotStartAtOne()
    {
        var rewards = new[] { Reward(1, 2, 9) };

        var verdict = Classify(rewards, roundCount: 9);

        await Assert.That(verdict.Classification).IsNotEqualTo(InstanceRewardSelectionClass.RoundBacked);
    }

    [Test]
    public async Task RoundBacked_ProvesSelectionButStaysUndeliverableWithoutATrigger()
    {
        var rewards = new[] { Reward(1, 1, 9) };

        var verdict = Classify(rewards, roundCount: 9, trigger: false);

        await Assert.That(verdict.Classification).IsEqualTo(InstanceRewardSelectionClass.RoundBacked);
        await Assert.That(verdict.SelectionSourceProven).IsTrue();
        await Assert.That(verdict.DeliverableNow).IsFalse();
        await Assert.That(verdict.Blocker).IsEqualTo(InstanceRewardBlocker.MissingDeliveryTrigger);
    }

    [Test]
    public async Task RoundBacked_RejectsAnInstanceWithNoAuthoredRounds()
    {
        var rewards = new[] { Reward(1, 1, 9) };

        var verdict = Classify(rewards, roundCount: 0);

        await Assert.That(verdict.Classification).IsNotEqualTo(InstanceRewardSelectionClass.RoundBacked);
    }

    [Test]
    public async Task RankBacked_ProvenByAFixedSizeTeamSpanningTheBands()
    {
        var rewards = new[] { Reward(1, 1, 1), Reward(2, 2, 2), Reward(3, 3, 3), Reward(4, 4, 4), Reward(5, 1, 4) };

        var verdict = Classify(rewards, teamSizes: [4], displaySurface: true);

        await Assert.That(verdict.Classification).IsEqualTo(InstanceRewardSelectionClass.RankBacked);
        await Assert.That(verdict.SelectionSourceProven).IsFalse();
        await Assert.That(verdict.DeliverableNow).IsFalse();
        await Assert.That(verdict.Blocker).IsEqualTo(InstanceRewardBlocker.MissingScoreSource);
    }

    [Test]
    public async Task RankBacked_IgnoresASinglePlayerTeam()
    {
        var rewards = new[] { Reward(1, 1, 1) };

        var verdict = Classify(rewards, teamSizes: [1], displaySurface: true);

        await Assert.That(verdict.Classification).IsNotEqualTo(InstanceRewardSelectionClass.RankBacked);
    }

    [Test]
    public async Task RankBacked_RejectsBandsThatDoNotSpanTheTeamSize()
    {
        var rewards = new[] { Reward(1, 1, 3) };

        var verdict = Classify(rewards, teamSizes: [4], displaySurface: true);

        await Assert.That(verdict.Classification).IsNotEqualTo(InstanceRewardSelectionClass.RankBacked);
    }

    [Test]
    public async Task RankBacked_RequiresADisplaySurfaceButNeverUsesItAsAScore()
    {
        var rewards = new[] { Reward(1, 1, 4) };

        var without = Classify(rewards, teamSizes: [4], displaySurface: false);
        var with = Classify(rewards, teamSizes: [4], displaySurface: true);

        await Assert.That(without.Classification).IsEqualTo(InstanceRewardSelectionClass.Unsupported);
        await Assert.That(with.Classification).IsEqualTo(InstanceRewardSelectionClass.RankBacked);
        await Assert.That(with.SelectionValue).IsEqualTo(0);
        await Assert.That(with.DeliverableNow).IsFalse();
    }

    [Test]
    public async Task Unsupported_WithoutAnySelectionEvidence()
    {
        var rewards = new[] { Reward(1, 1, 4) };

        var verdict = Classify(rewards);

        await Assert.That(verdict.Classification).IsEqualTo(InstanceRewardSelectionClass.Unsupported);
        await Assert.That(verdict.Blocker).IsEqualTo(InstanceRewardBlocker.MissingSelectionSource);
        await Assert.That(verdict.DeliverableNow).IsFalse();
    }

    [Test]
    public async Task Unsupported_WhenNoRewardRowIsPublishedForTheKind()
    {
        var verdict = Classify([]);

        await Assert.That(verdict.Classification).IsEqualTo(InstanceRewardSelectionClass.Unsupported);
        await Assert.That(verdict.Blocker).IsEqualTo(InstanceRewardBlocker.MissingRewardRows);
    }

    /// <summary>
    /// The classification must be structural. Renaming or renumbering a reward kind cannot move it
    /// between classes, because no name or literal id takes part in any test above.
    /// </summary>
    [Test]
    public async Task Classification_DoesNotDependOnTheKindNameOrId()
    {
        var rewards = new[] { Reward(1, 1, 4) };
        var renamed = Classify(rewards, teamSizes: [4], displaySurface: true,
            kind: 4242, kindName: "a_completely_different_name");
        var original = Classify(rewards, teamSizes: [4], displaySurface: true,
            kind: 7, kindName: "soldier_rank");

        await Assert.That(renamed.Classification).IsEqualTo(original.Classification);
        await Assert.That(renamed.Blocker).IsEqualTo(original.Blocker);
    }

    [Test]
    public async Task DescribeBlocker_NamesTheExactMissingArtifact()
    {
        var rewards = new[] { Reward(1, 1, 4) };

        var roundBacked = Classify(rewards, roundCount: 4, trigger: false);
        var rankBacked = Classify(rewards, teamSizes: [4], displaySurface: true);
        var unsupported = Classify(rewards);
        var noRows = Classify([]);

        await Assert.That(InstanceRewardTaxonomyRules.DescribeBlocker(roundBacked))
            .Contains("indun_action_send_mail_rewards");
        await Assert.That(InstanceRewardTaxonomyRules.DescribeBlocker(rankBacked))
            .Contains("score");
        await Assert.That(InstanceRewardTaxonomyRules.DescribeBlocker(unsupported))
            .Contains("indun_rounds");
        await Assert.That(InstanceRewardTaxonomyRules.DescribeBlocker(noRows))
            .Contains("reward row");
    }

    [Test]
    public async Task DescribeBlocker_IsEmptyWhenNothingBlocks()
    {
        var verdict = Classify([Reward(1, 1, 12)], [1, 2, 3]);

        await Assert.That(InstanceRewardTaxonomyRules.DescribeBlocker(verdict)).IsEmpty();
    }
}
