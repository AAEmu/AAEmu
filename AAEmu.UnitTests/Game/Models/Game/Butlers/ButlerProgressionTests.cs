using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.UnitTests.Game.Models.Game.Butlers;

public class ButlerProgressionTests
{
    [Test]
    public async Task TryAddExperience_AcceptsOnlyPositiveValuesAndPreservesCurrentOnFailure()
    {
        await Assert.That(ButlerProgression.TryAddExperience(100, 25, out var updated)).IsTrue();
        await Assert.That(updated).IsEqualTo(125ul);

        await Assert.That(ButlerProgression.TryAddExperience(100, 0, out updated)).IsFalse();
        await Assert.That(updated).IsEqualTo(100ul);
        await Assert.That(ButlerProgression.TryAddExperience(100, -1, out updated)).IsFalse();
        await Assert.That(updated).IsEqualTo(100ul);
        await Assert.That(ButlerProgression.TryAddExperience(ulong.MaxValue, 1, out updated)).IsFalse();
        await Assert.That(updated).IsEqualTo(ulong.MaxValue);
    }
}
