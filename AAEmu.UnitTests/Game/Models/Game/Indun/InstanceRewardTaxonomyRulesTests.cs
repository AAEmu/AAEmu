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
    public async Task DifficultyBacked_BeforeSelectionCarriesNoDeliverableValue()
    {
        // The class is still provable - the instance publishes difficulties and the authored band covers
        // them - but "provable" is not "chosen", so there is nothing to pay out with.
        var rewards = new[] { Reward(1, 1, 12) };
        var difficulties = new byte[] { 5, 7, 9 };

        var verdict = Classify(rewards, difficulties);

        await Assert.That(verdict.Classification).IsEqualTo(InstanceRewardSelectionClass.DifficultyBacked);
        await Assert.That(verdict.SelectionValue).IsEqualTo(0);
        await Assert.That(verdict.DeliverableNow).IsFalse();
    }

    [Test]
    public async Task DifficultyBacked_DoesNotSubstituteALowerBandForAnOutOfRangeChoice()
    {
        // A chosen difficulty outside every authored band names nothing this run can be paid from, so
        // the verdict is refused rather than answered with the lowest authored one.
        var rewards = new[] { Reward(1, 1, 3) };
        var difficulties = new byte[] { 1, 2, 3 };

        var verdict = Classify(rewards, difficulties, runtimeDifficulty: 99);

        await Assert.That(verdict.Classification).IsEqualTo(InstanceRewardSelectionClass.DifficultyBacked);
        await Assert.That(verdict.SelectionValue).IsEqualTo(0);
        await Assert.That(verdict.DeliverableNow).IsFalse();
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
        // A genuinely unblocked verdict is one where a difficulty this run chose landed in an
        // authored band, so a value exists and a trigger is authored.
        var verdict = Classify([Reward(1, 1, 12)], [1, 2, 3], runtimeDifficulty: 2);

        await Assert.That(verdict.DeliverableNow).IsTrue();
        await Assert.That(InstanceRewardTaxonomyRules.DescribeBlocker(verdict)).IsEmpty();
    }
    /// <summary>
    /// A difficulty-backed copy that has not chosen a difficulty is not deliverable.
    /// </summary>
    /// <remarks>
    /// The defect this pins: the classifier fell back to the lowest authored difficulty so the class
    /// stayed provable, and the mail action delivers on <c>verdict.SelectionValue</c> - so a run that
    /// never chose a difficulty was paid the easiest reward. Proving the class is a content question;
    /// naming the value is a runtime one, and only the second can pay out.
    /// </remarks>
    [Test]
    public async Task DifficultyBacked_WithoutAChosenDifficultyIsNotDeliverable()
    {
        var rewards = new[] { Reward(1, 1, 1), Reward(2, 3, 3) };
        var difficulties = new byte[] { 1, 3 };

        var verdict = Classify(rewards, difficulties, runtimeDifficulty: null);

        // Still classified as difficulty-backed: that is a fact about content and does not change.
        await Assert.That(verdict.Classification).IsEqualTo(InstanceRewardSelectionClass.DifficultyBacked);
        await Assert.That(verdict.SelectionSourceProven).IsTrue();

        // But there is no value to pay out with, and it says why rather than quietly defaulting.
        await Assert.That(verdict.DeliverableNow).IsFalse();
        await Assert.That(verdict.Blocker).IsEqualTo(InstanceRewardBlocker.MissingSelectionSource);
    }

    /// <summary>
    /// Swept over the ways "no chosen difficulty" arrives: absent, zero, and present but landing in no
    /// authored band. None of them may be answered with a difficulty the run never picked.
    /// </summary>
    [Test]
    public async Task NoDifficultyThatNamesThisRunIsNeverDeliverable()
    {
        var rewards = new[] { Reward(1, 3, 3) };
        var difficulties = new byte[] { 1, 3 };

        foreach (var chosen in new byte?[] { null, 0, 2, 9 })
        {
            var verdict = Classify(rewards, difficulties, runtimeDifficulty: chosen);

            await Assert.That(verdict.DeliverableNow).IsFalse();
        }
    }

    /// <summary>
    /// The normal case is untouched: a chosen difficulty inside an authored band is deliverable and
    /// carries that value. Without this the fix could be passing by refusing everything.
    /// </summary>
    [Test]
    public async Task AChosenDifficultyInABandIsStillDeliveredWithThatValue()
    {
        var rewards = new[] { Reward(1, 1, 1), Reward(2, 3, 3) };
        var difficulties = new byte[] { 1, 3 };

        foreach (var chosen in new byte[] { 1, 3 })
        {
            var verdict = Classify(rewards, difficulties, runtimeDifficulty: chosen);

            await Assert.That(verdict.DeliverableNow).IsTrue();
            await Assert.That(verdict.SelectionValue).IsEqualTo(chosen);
        }
    }

    /// <summary>
    /// A missing delivery trigger still blocks on its own, and a chosen difficulty must not paper over
    /// it - the two blockers are independent.
    /// </summary>
    [Test]
    public async Task AChosenDifficultyStillCannotDeliverWithoutATrigger()
    {
        var rewards = new[] { Reward(1, 3, 3) };

        var verdict = Classify(rewards, new byte[] { 3 }, trigger: false, runtimeDifficulty: 3);

        await Assert.That(verdict.DeliverableNow).IsFalse();
        await Assert.That(verdict.Blocker).IsEqualTo(InstanceRewardBlocker.MissingDeliveryTrigger);
    }

}
