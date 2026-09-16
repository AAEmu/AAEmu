using AAEmu.Game.Models.Game.Char;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class CharacterBotCheckTests
{
    [Test]
    public async Task NewCharacter_HasNothingPendingAndIsNotSuspect()
    {
        var check = new CharacterBotCheck();

        await Assert.That(check.RemainChecks).IsEqualTo((byte)0);
        await Assert.That(check.FailedAnswers).IsEqualTo((short)0);
        await Assert.That(check.OnTrial).IsFalse();
    }

    [Test]
    public async Task MarkOnTrial_FlagsTheCharacterOnce()
    {
        var check = new CharacterBotCheck();

        await Assert.That(check.MarkOnTrial()).IsTrue();
        await Assert.That(check.OnTrial).IsTrue();

        // flagging an already suspected character is not a second trial
        await Assert.That(check.MarkOnTrial()).IsFalse();
    }

    [Test]
    public async Task RecordAnswer_SpendsACheckAndRefusesWhenNoneIsPending()
    {
        var check = new CharacterBotCheck();

        // nothing pending: an answer that cannot belong to a check is refused, not counted
        await Assert.That(check.RecordAnswer()).IsFalse();
        await Assert.That(check.RemainChecks).IsEqualTo((byte)0);
        await Assert.That(check.FailedAnswers).IsEqualTo((short)0);
    }
}
