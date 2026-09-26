using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.UnitTests.Game.Models.Game.Butlers;

public class ButlerSpecialtyTradeStateTests
{
    [Test]
    public async Task LoadedState_RoundTripsDurableSpecialtyJobsAndOwnedCancellation()
    {
        var butler = new CharacterButler(42);
        butler.Apply(new CharacterButlerRecord(42, 10, "Farmhand", 100, 0, 100, 0));
        butler.ApplyLoadedState(new CharacterButlerStateRecord(
            butler.Snapshot(),
            new Dictionary<sbyte, ulong>(),
            [],
            [],
            [new ButlerSpecialtyTradeJob(77, 20, 9, 4, 7002, 100, 25)]));

        await Assert.That(butler.SpecialtyTradeJobs).Count().IsEqualTo(1);
        await Assert.That(butler.SpecialtyTradeJobs[77].SpecialtyType).IsEqualTo(9u);
        await Assert.That(butler.SpecialtyTradeJobs[77].ProductItemId).IsEqualTo(7002u);
        await Assert.That(butler.SnapshotSpecialtyTradeJobs()).HasCount().EqualTo(1);
        await Assert.That(butler.RemoveSpecialtyTradeJob(77)).IsTrue();
        await Assert.That(butler.SpecialtyTradeJobs).IsEmpty();
        await Assert.That(butler.RemoveSpecialtyTradeJob(77)).IsFalse();
    }
}
