using AAEmu.World.Core.Zone;

namespace AAEmu.UnitTests.World.Core.Zone;

public class UnitRegistryTests
{
    [Test]
    public async Task ExhaustedLiveSkips_DropsTheUnitInsteadOfReusingAnOccupiedId()
    {
        var registry = new UnitRegistry();
        var allocated = 0u;
        var calls = 0;

        var bcId = registry.Register(
            [1, 2, 3],
            () =>
            {
                calls++;
                return ++allocated;
            },
            _ => true);

        await Assert.That(bcId).IsEqualTo(0u);
        await Assert.That(registry.Count).IsEqualTo(0);
        await Assert.That(calls).IsGreaterThan(1);
    }

    [Test]
    public async Task FirstIdGameDoesNotOwn_IsRegistered()
    {
        var registry = new UnitRegistry();
        var allocated = 0u;

        var bcId = registry.Register([4, 5, 6], () => ++allocated, candidate => candidate < 3);

        await Assert.That(bcId).IsEqualTo(3u);
        await Assert.That(registry.Count).IsEqualTo(1);
        await Assert.That(registry.Contains(3)).IsTrue();
        await Assert.That(registry.TryGet(3, out var body)).IsTrue();
        await Assert.That(Hex.Of(body)).IsEqualTo(Hex.Of(new byte[] { 4, 5, 6 }));
    }
}
