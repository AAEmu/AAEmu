using AAEmu.Game.Models.Game.Justice;

namespace AAEmu.UnitTests.Game.Models.Game.Justice;

/// <summary>
/// The bench's votes live on the jurors, so the tally is whatever the seated bench holds right now. A
/// juror who is released or leaves takes their vote with them: a trial-level counter would let a juror
/// vote the top tier, walk out, and still decide the verdict and the sentence from outside the court.
/// </summary>
public class TrialBenchVoteTests
{
    [Test]
    public async Task Tally_ReadsTheSeatedBenchOnly()
    {
        var trial = CreateTrial();
        var guilty = Seat(trial, 11, choice: TrialVerdictRules.FirstGuiltyChoice + 2);
        Seat(trial, 12, choice: TrialVerdictRules.NotGuiltyChoice);
        var silent = Seat(trial, 13);

        await Assert.That(trial.GuiltyVotes).IsEqualTo(1);
        await Assert.That(trial.NotGuiltyVotes).IsEqualTo(1);
        await Assert.That(trial.VotedCount).IsEqualTo(2);
        await Assert.That(trial.HighestGuiltyChoice).IsEqualTo(guilty.Choice);
        await Assert.That(silent.Voted).IsFalse();

        // Two of three chairs answered, so the case is not decided yet.
        await Assert.That(TrialVerdictRules.Tally(trial.GuiltyVotes, trial.NotGuiltyVotes, trial.Jurors.Count))
            .IsEqualTo(TrialVerdict.Pending);
    }

    [Test]
    public async Task ReleasedJuror_TakesTheirVoteAndTheRulingWithThem()
    {
        var trial = CreateTrial();
        var heavyVoter = Seat(trial, 11, choice: TrialVerdictRules.LastGuiltyChoice);
        Seat(trial, 12, choice: TrialVerdictRules.NotGuiltyChoice);
        Seat(trial, 13, choice: TrialVerdictRules.NotGuiltyChoice);

        await Assert.That(TrialVerdictRules.Tally(trial.GuiltyVotes, trial.NotGuiltyVotes, trial.Jurors.Count))
            .IsEqualTo(TrialVerdict.NotGuilty);

        trial.Jurors.Remove(heavyVoter);

        await Assert.That(trial.GuiltyVotes).IsEqualTo(0);
        await Assert.That(trial.HighestGuiltyChoice).IsEqualTo((byte)0);
        await Assert.That(trial.VotedCount).IsEqualTo(2);

        var verdict = TrialVerdictRules.Tally(trial.GuiltyVotes, trial.NotGuiltyVotes, trial.Jurors.Count);
        await Assert.That(verdict).IsEqualTo(TrialVerdict.NotGuilty);
        await Assert.That(TrialVerdictRules.RulingChoice(verdict, trial.HighestGuiltyChoice))
            .IsEqualTo(TrialVerdictRules.NotGuiltyChoice);
    }

    [Test]
    public async Task HighestGuiltyChoice_IsTheHeaviestRowStillOnTheBench()
    {
        var trial = CreateTrial();
        var heaviest = Seat(trial, 11, choice: TrialVerdictRules.LastGuiltyChoice);
        Seat(trial, 12, choice: TrialVerdictRules.FirstGuiltyChoice + 1);

        await Assert.That(trial.HighestGuiltyChoice).IsEqualTo(TrialVerdictRules.LastGuiltyChoice);

        trial.Jurors.Remove(heaviest);

        await Assert.That(trial.HighestGuiltyChoice)
            .IsEqualTo((byte)(TrialVerdictRules.FirstGuiltyChoice + 1));
    }

    private static Trial CreateTrial() => new() { Id = 1, Court = 0, DefendantId = 9001 };

    private static TrialJuror Seat(Trial trial, uint characterId, byte choice = 0)
    {
        var seat = new TrialJuror { CharacterId = characterId, Court = trial.Court, Seat = trial.Jurors.Count };
        seat.Choice = choice;
        trial.Jurors.Add(seat);
        return seat;
    }
}
