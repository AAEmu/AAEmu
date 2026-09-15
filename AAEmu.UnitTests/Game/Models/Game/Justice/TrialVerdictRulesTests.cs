using AAEmu.Game.Models.Game.Justice;

namespace AAEmu.UnitTests.Game.Models.Game.Justice;

public class TrialVerdictRulesTests
{
    [Test]
    public async Task Tally_WaitsUntilEverySeatedJurorHasVoted()
    {
        await Assert.That(TrialVerdictRules.Tally(2, 0, 6)).IsEqualTo(TrialVerdict.Pending);
        await Assert.That(TrialVerdictRules.Tally(6, 0, 6)).IsEqualTo(TrialVerdict.Guilty);
    }

    [Test]
    public async Task Tally_MajorityWins_TieAcquits()
    {
        await Assert.That(TrialVerdictRules.Tally(4, 2, 6)).IsEqualTo(TrialVerdict.Guilty);
        await Assert.That(TrialVerdictRules.Tally(2, 4, 6)).IsEqualTo(TrialVerdict.NotGuilty);
        await Assert.That(TrialVerdictRules.Tally(3, 3, 6)).IsEqualTo(TrialVerdict.NotGuilty);
    }

    [Test]
    public async Task Tally_NoBench_NeverConvicts()
    {
        // A trial with nobody seated stays pending - it must not auto-convict the defendant.
        await Assert.That(TrialVerdictRules.Tally(0, 0, 0)).IsEqualTo(TrialVerdict.Pending);
    }

    [Test]
    public async Task Seats_MatchTheClientsOwnTable()
    {
        // The client indexes 5 * court + juryNumber with juryNumber 0-4.
        await Assert.That(TrialVerdictRules.IsValidSeat(0, 0)).IsTrue();
        await Assert.That(TrialVerdictRules.IsValidSeat(1, 4)).IsTrue();
        await Assert.That(TrialVerdictRules.IsValidSeat(0, 5)).IsFalse();
        await Assert.That(TrialVerdictRules.IsValidSeat(-1, 0)).IsFalse();
    }

    [Test]
    public async Task Vote_TheClientsFirstRowIsNotGuilty()
    {
        // The verdict window's own row constants: 1 not guilty, 2..6 the five guilty tiers. Reading
        // the byte as "above zero is guilty" would turn every not-guilty vote into a conviction.
        await Assert.That(TrialVerdictRules.IsNotGuiltyChoice(1)).IsTrue();
        await Assert.That(TrialVerdictRules.IsGuiltyChoice(1)).IsFalse();

        await Assert.That(TrialVerdictRules.IsGuiltyChoice(2)).IsTrue();
        await Assert.That(TrialVerdictRules.IsGuiltyChoice(6)).IsTrue();
        await Assert.That(TrialVerdictRules.IsNotGuiltyChoice(6)).IsFalse();
    }

    [Test]
    public async Task Vote_OnlyTheRowsTheWindowCanSendAreAccepted()
    {
        await Assert.That(TrialVerdictRules.IsValidChoice(0)).IsFalse();
        await Assert.That(TrialVerdictRules.IsValidChoice(1)).IsTrue();
        await Assert.That(TrialVerdictRules.IsValidChoice(6)).IsTrue();
        await Assert.That(TrialVerdictRules.IsValidChoice(7)).IsFalse();
    }

    [Test]
    public async Task Vote_GuiltyRowsCountAsOneThroughFive()
    {
        await Assert.That(TrialVerdictRules.GuiltyTier(2)).IsEqualTo(1);
        await Assert.That(TrialVerdictRules.GuiltyTier(6)).IsEqualTo(5);
        await Assert.That(TrialVerdictRules.GuiltyTier(1)).IsEqualTo(0);
    }

    [Test]
    public async Task Ruling_ReadsTheHeaviestGuiltyRow_OrAcquits()
    {
        await Assert.That(TrialVerdictRules.RulingChoice(TrialVerdict.Guilty, 4)).IsEqualTo((byte)4);
        await Assert.That(TrialVerdictRules.RulingChoice(TrialVerdict.NotGuilty, 6))
            .IsEqualTo(TrialVerdictRules.NotGuiltyChoice);
        // A guilty verdict with no guilty row behind it can only be read as the not-guilty constant.
        await Assert.That(TrialVerdictRules.RulingChoice(TrialVerdict.Guilty, 0))
            .IsEqualTo(TrialVerdictRules.NotGuiltyChoice);
    }

    [Test]
    public async Task States_AreTheNumbersTheClientSwitchesOn()
    {
        // The client's own X2Trial constants, in the order its step ribbon reads them. A trial that
        // used different numbering would drive the wrong window at every phase.
        await Assert.That((byte)TrialState.Free).IsEqualTo((byte)0);
        await Assert.That((byte)TrialState.WaitingCrimeRecord).IsEqualTo((byte)1);
        await Assert.That((byte)TrialState.WaitingJury).IsEqualTo((byte)2);
        await Assert.That((byte)TrialState.Testimony).IsEqualTo((byte)3);
        await Assert.That((byte)TrialState.FinalStatement).IsEqualTo((byte)4);
        await Assert.That((byte)TrialState.Sentence).IsEqualTo((byte)5);
        await Assert.That((byte)TrialState.GuiltyBySystem).IsEqualTo((byte)6);
        await Assert.That((byte)TrialState.GuiltyByUser).IsEqualTo((byte)7);
        await Assert.That((byte)TrialState.PostSentence).IsEqualTo((byte)8);
    }
}
