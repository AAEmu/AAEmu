using AAEmu.Game.Models.Game.Justice;

namespace AAEmu.UnitTests.Game.Models.Game.Justice;

public class TrialCaseFileTests
{
    private static Trial NewTrial() => new() { Id = 1, DefendantId = 7, DefendantName = "Tester", Court = 0 };

    [Test]
    public async Task OpenCaseFile_FixesTheCrimesTheCaseIsAbout()
    {
        var trial = NewTrial();
        trial.OpenCaseFile([11u, 12u]);

        await Assert.That(trial.CaseFileOpened).IsTrue();
        await Assert.That(trial.IsTriedCrime(11u)).IsTrue();
        await Assert.That(trial.IsTriedCrime(13u)).IsFalse();
    }

    [Test]
    public async Task OpenCaseFile_IsOnlyOpenedOnce()
    {
        // A second reader - a juror seated late, an onlooker - reads the same file, so a crime that
        // turns up in the meantime must not join the case.
        var trial = NewTrial();
        trial.OpenCaseFile([11u]);
        trial.OpenCaseFile([11u, 99u]);

        await Assert.That(trial.IsTriedCrime(99u)).IsFalse();
        await Assert.That(trial.TriedCrimeIds.Count).IsEqualTo(1);
    }

    [Test]
    public async Task OpenCaseFile_AnEmptyFileStaysEmpty()
    {
        // A defendant with nothing on the books opens an empty case, and the empty file is still the
        // case: a crime reported while the bench is sitting must not fill it in.
        var trial = NewTrial();
        trial.OpenCaseFile([]);
        trial.OpenCaseFile([42u]);

        await Assert.That(trial.CaseFileOpened).IsTrue();
        await Assert.That(trial.TriedCrimeIds).IsEmpty();
        await Assert.That(trial.IsTriedCrime(42u)).IsFalse();
    }
}
