using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Justice;

namespace AAEmu.UnitTests.Game.Models.Game.Justice;

/// <summary>
/// The five guilty rows of the court's verdict window are shares of the case's base sentence, and the
/// shares are shipped content_configs rows. These tests seed the shipped numbers and check the court
/// reads a row as that share - a base sentence handed to the client in milliseconds is what the bench
/// is really choosing between.
/// </summary>
public class TrialSentenceRulesTests
{
    private const uint BaseSentenceMs = 1_800_000u; // 30 minutes

    [Before(Test)]
    public void SeedShippedRatios()
    {
        var data = ContentConfigGameData.Instance;
        data.SetForTest("trial_sentence_ratio_range_1", 50);
        data.SetForTest("trial_sentence_ratio_range_2", 80);
        data.SetForTest("trial_sentence_ratio_range_3", 100);
        data.SetForTest("trial_sentence_ratio_range_4", 130);
        data.SetForTest("trial_sentence_ratio_range_5", 150);
    }

    [Test]
    public async Task Ratio_ReadsTheRowsShare()
    {
        await Assert.That(TrialSentenceRules.RatioPercent(2)).IsEqualTo(50);
        await Assert.That(TrialSentenceRules.RatioPercent(4)).IsEqualTo(100);
        await Assert.That(TrialSentenceRules.RatioPercent(6)).IsEqualTo(150);
        // The not-guilty row is not a sentence at all.
        await Assert.That(TrialSentenceRules.RatioPercent(TrialVerdictRules.NotGuiltyChoice)).IsEqualTo(0);
        await Assert.That(TrialSentenceRules.RatioPercent(0)).IsEqualTo(0);
    }

    [Test]
    public async Task Sentence_ScalesTheBaseTheClientsWay()
    {
        // The client builds row N as (baseMs * percent) / 100 / 60000 whole minutes.
        await Assert.That(TrialSentenceRules.SentenceMinutes(BaseSentenceMs, 2)).IsEqualTo(15);
        await Assert.That(TrialSentenceRules.SentenceMinutes(BaseSentenceMs, 3)).IsEqualTo(24);
        await Assert.That(TrialSentenceRules.SentenceMinutes(BaseSentenceMs, 4)).IsEqualTo(30);
        await Assert.That(TrialSentenceRules.SentenceMinutes(BaseSentenceMs, 5)).IsEqualTo(39);
        await Assert.That(TrialSentenceRules.SentenceMinutes(BaseSentenceMs, 6)).IsEqualTo(45);
    }

    [Test]
    public async Task Sentence_DoesNotRoundTheMiddleRowAway()
    {
        await Assert.That(TrialSentenceRules.SentenceMilliseconds(BaseSentenceMs, 3)).IsEqualTo(1_440_000u);
        await Assert.That(TrialSentenceRules.SentenceMilliseconds(BaseSentenceMs, 4)).IsEqualTo(1_800_000u);
    }

    [Test]
    public async Task BaseSentenceRow_IsTheHundredPercentRow()
    {
        // Giving up and "the trial cannot continue" both hand out the plain base sentence, whichever
        // row that turns out to be in the shipped ratios.
        var choice = TrialSentenceRules.BaseSentenceChoice();

        await Assert.That(TrialVerdictRules.IsGuiltyChoice(choice)).IsTrue();
        await Assert.That(TrialSentenceRules.RatioPercent(choice)).IsEqualTo(100);
        await Assert.That(TrialSentenceRules.SentenceMilliseconds(BaseSentenceMs, choice))
            .IsEqualTo(BaseSentenceMs);
    }
}
