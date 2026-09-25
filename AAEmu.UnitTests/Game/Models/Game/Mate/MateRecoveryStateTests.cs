using AAEmu.Game.Models.Game.Mate;

namespace AAEmu.UnitTests.Game.Models.Game.Mates;

public class MateRecoveryStateTests
{
    [Test]
    public async Task FromPersisted_AllNull_KeepsLegacyRowUnresolved()
    {
        var state = MateRecoveryState.FromPersisted(null, null, null);

        await Assert.That(state.HasValue).IsFalse();
    }

    [Test]
    public async Task FromPersisted_AllValues_ReturnsExactSnapshot()
    {
        var state = MateRecoveryState.FromPersisted(17, 23, 29);

        await Assert.That(state.HasValue).IsTrue();
        await Assert.That(state!.Value.MateReviveDelay).IsEqualTo(17);
        await Assert.That(state.Value.MateReviveHpPercent).IsEqualTo(23);
        await Assert.That(state.Value.MateReviveMpPercent).IsEqualTo(29);
    }

    [Test]
    public async Task FromPersisted_PartialSnapshot_RefusesFallback()
    {
        Assert.Throws<InvalidDataException>(() => MateRecoveryState.FromPersisted(17, null, 29));
        Assert.Throws<InvalidDataException>(() => MateRecoveryState.FromPersisted(null, 23, 29));
        Assert.Throws<InvalidDataException>(() => MateRecoveryState.FromPersisted(17, 23, null));
    }
}
