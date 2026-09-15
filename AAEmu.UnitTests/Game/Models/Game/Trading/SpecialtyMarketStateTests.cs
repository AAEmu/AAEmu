using AAEmu.Game.Models.Game.Trading;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Trading;

public class SpecialtyMarketStateTests
{
    [Test]
    public async Task Clone_CopiesEveryCollectionAndRecord()
    {
        var original = CreateState();
        var clone = original.Clone();

        await Assert.That(clone.Revision).IsEqualTo(original.Revision);
        await Assert.That(ReferenceEquals(clone, original)).IsFalse();
        await Assert.That(ReferenceEquals(clone.PriceRatios, original.PriceRatios)).IsFalse();
        await Assert.That(ReferenceEquals(clone.DemandRemainders, original.DemandRemainders)).IsFalse();
        await Assert.That(ReferenceEquals(clone.MaterialContributions, original.MaterialContributions)).IsFalse();
        await Assert.That(ReferenceEquals(clone.MaterialContributions[(2, 3)], original.MaterialContributions[(2, 3)])).IsFalse();
        await Assert.That(ReferenceEquals(clone.MaterialContributions[(2, 3)][0], original.MaterialContributions[(2, 3)][0])).IsFalse();
        await Assert.That(ReferenceEquals(clone.CargoStock, original.CargoStock)).IsFalse();
        await Assert.That(ReferenceEquals(clone.StockEventNextChecks, original.StockEventNextChecks)).IsFalse();
        await Assert.That(ReferenceEquals(clone.StockEventActivations, original.StockEventActivations)).IsFalse();
        await Assert.That(ReferenceEquals(clone.Records, original.Records)).IsFalse();
        await Assert.That(ReferenceEquals(clone.Records[(1, 2)][0], original.Records[(1, 2)][0])).IsFalse();
        await Assert.That(clone.Records[(1, 2)][0].Ratio).IsEqualTo(100);
        await Assert.That(clone.Records[(1, 2)][0].Recorded).IsEqualTo(10L);

        clone.Revision++;
        clone.PriceRatios[1][2] = 50;
        clone.DemandRemainders[1][2]++;
        clone.MaterialContributions[(2, 3)][0] = new SpecialtyMaterialContribution(1, 9, 5);
        clone.CargoStock[(2, 4)]++;
        clone.StockEventNextChecks[1]++;
        clone.StockEventActivations[2] = new SpecialtyStockEventActivation(20, 40);
        clone.Records[(1, 2)].Add(new SpecialtyMarketRecord(50, 11));

        await Assert.That(original.Revision).IsEqualTo(7L);
        await Assert.That(original.PriceRatios[1][2]).IsEqualTo(100);
        await Assert.That(original.DemandRemainders[1][2]).IsEqualTo(3);
        await Assert.That(original.MaterialContributions[(2, 3)][0].Amount).IsEqualTo(4U);
        await Assert.That(original.CargoStock[(2, 4)]).IsEqualTo(5U);
        await Assert.That(original.StockEventNextChecks[1]).IsEqualTo(20L);
        await Assert.That(original.StockEventActivations[2]).IsEqualTo(new SpecialtyStockEventActivation(10, 30));
        await Assert.That(original.Records[(1, 2)].Count).IsEqualTo(1);
    }

    [Test]
    public async Task NewState_HasIndependentEmptyCollectionsAndZeroRevision()
    {
        var first = new SpecialtyMarketState();
        var second = new SpecialtyMarketState();
        var clone = first.Clone();
        first.PriceRatios.Add(1, []);
        first.DemandRemainders.Add(1, []);
        first.MaterialContributions.Add((2, 3), [new SpecialtyMaterialContribution(1, 1, 1)]);
        first.CargoStock.Add((2, 4), 0);
        first.StockEventNextChecks.Add(1, 20);
        first.StockEventActivations.Add(2, new SpecialtyStockEventActivation(10, 30));
        first.Records.Add((1, 2), []);

        foreach (var state in new[] { second, clone })
        {
            await Assert.That(state.Revision).IsEqualTo(0L);
            await Assert.That(state.PriceRatios.Count).IsEqualTo(0);
            await Assert.That(state.DemandRemainders.Count).IsEqualTo(0);
            await Assert.That(state.MaterialContributions.Count).IsEqualTo(0);
            await Assert.That(state.CargoStock.Count).IsEqualTo(0);
            await Assert.That(state.StockEventNextChecks.Count).IsEqualTo(0);
            await Assert.That(state.StockEventActivations.Count).IsEqualTo(0);
            await Assert.That(state.Records.Count).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments("negative-ratio")]
    [Arguments("negative-remainder")]
    [Arguments("orphan-pending-item")]
    [Arguments("orphan-zero-pending-zone")]
    [Arguments("zero-ratio-item")]
    [Arguments("zero-ratio-zone")]
    [Arguments("zero-pending-item")]
    [Arguments("zero-pending-zone")]
    [Arguments("zero-material-zone")]
    [Arguments("zero-material-tag")]
    [Arguments("empty-material-queue")]
    [Arguments("zero-material-sequence")]
    [Arguments("descending-material-sequence")]
    [Arguments("zero-material-item")]
    [Arguments("zero-material-amount")]
    [Arguments("overflow-material-total")]
    [Arguments("null-material-queue")]
    [Arguments("null-material-contribution")]
    [Arguments("zero-cargo-zone")]
    [Arguments("zero-cargo-good")]
    [Arguments("zero-history-item")]
    [Arguments("zero-history-zone")]
    [Arguments("negative-history-ratio")]
    [Arguments("negative-history-time")]
    [Arguments("descending-history")]
    [Arguments("oversized-history")]
    [Arguments("null-ratios")]
    [Arguments("null-pending")]
    [Arguments("null-history")]
    [Arguments("null-record")]
    [Arguments("zero-stock-trigger")]
    [Arguments("negative-next-check")]
    [Arguments("zero-stock-event")]
    [Arguments("null-stock-event")]
    [Arguments("invalid-stock-event-time")]
    public void InvalidState_IsRejectedInBothSnapshotsBeforeDatabaseAccess(string corruption)
    {
        var invalid = CreateState();
        switch (corruption)
        {
            case "negative-ratio": invalid.PriceRatios[1][2] = -1; break;
            case "negative-remainder": invalid.DemandRemainders[1][2] = -1; break;
            case "orphan-pending-item": invalid.DemandRemainders[9] = new() { [2] = 1 }; break;
            case "orphan-zero-pending-zone": invalid.DemandRemainders[1][9] = 0; break;
            case "zero-ratio-item": invalid.PriceRatios[0] = []; break;
            case "zero-ratio-zone": invalid.PriceRatios[1][0] = 1; break;
            case "zero-pending-item": invalid.DemandRemainders[0] = []; break;
            case "zero-pending-zone": invalid.DemandRemainders[1][0] = 0; break;
            case "zero-material-zone": invalid.MaterialContributions[(0, 3)] = [new(1, 1, 1)]; break;
            case "zero-material-tag": invalid.MaterialContributions[(2, 0)] = [new(1, 1, 1)]; break;
            case "empty-material-queue": invalid.MaterialContributions[(2, 3)] = []; break;
            case "zero-material-sequence": invalid.MaterialContributions[(2, 3)] = [new(0, 1, 1)]; break;
            case "descending-material-sequence": invalid.MaterialContributions[(2, 3)].Add(new(1, 1, 1)); break;
            case "zero-material-item": invalid.MaterialContributions[(2, 3)] = [new(1, 0, 1)]; break;
            case "zero-material-amount": invalid.MaterialContributions[(2, 3)] = [new(1, 1, 0)]; break;
            case "overflow-material-total": invalid.MaterialContributions[(2, 3)].Add(new(2, 1, uint.MaxValue)); break;
            case "null-material-queue": invalid.MaterialContributions[(2, 3)] = null; break;
            case "null-material-contribution": invalid.MaterialContributions[(2, 3)].Add(null); break;
            case "zero-cargo-zone": invalid.CargoStock[(0, 4)] = 0; break;
            case "zero-cargo-good": invalid.CargoStock[(2, 0)] = 0; break;
            case "zero-history-item": invalid.Records[(0, 2)] = []; break;
            case "zero-history-zone": invalid.Records[(1, 0)] = []; break;
            case "negative-history-ratio": invalid.Records[(1, 2)] = [new(-1, 0)]; break;
            case "negative-history-time": invalid.Records[(1, 2)] = [new(0, -1)]; break;
            case "descending-history": invalid.Records[(1, 2)].Add(new(100, 9)); break;
            case "oversized-history": invalid.Records[(1, 2)] = Enumerable.Range(0, 257).Select(i => new SpecialtyMarketRecord(100, i)).ToList(); break;
            case "null-ratios": invalid.PriceRatios[1] = null; break;
            case "null-pending": invalid.DemandRemainders[1] = null; break;
            case "null-history": invalid.Records[(1, 2)] = null; break;
            case "null-record": invalid.Records[(1, 2)].Add(null); break;
            case "zero-stock-trigger": invalid.StockEventNextChecks[0] = 10; break;
            case "negative-next-check": invalid.StockEventNextChecks[1] = -1; break;
            case "zero-stock-event": invalid.StockEventActivations[0] = new(10, 20); break;
            case "null-stock-event": invalid.StockEventActivations[1] = null; break;
            case "invalid-stock-event-time": invalid.StockEventActivations[1] = new(20, 20); break;
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }

        var store = new MySqlSpecialtyMarketStore(new RecordingSaveManager());
        var valid = CreateState();
        valid.Revision = invalid.Revision + 1;
        var invalidExpected = new SpecialtyMarketWrite(invalid, valid);
        Assert.Throws<ArgumentException>(() => store.Commit(invalidExpected));
        Assert.Throws<ArgumentException>(() => store.Apply(null, null, invalidExpected));

        valid.Revision = invalid.Revision - 1;
        var invalidUpdated = new SpecialtyMarketWrite(valid, invalid);
        Assert.Throws<ArgumentException>(() => store.Commit(invalidUpdated));
        Assert.Throws<ArgumentException>(() => store.Apply(null, null, invalidUpdated));
    }

    [Test]
    public void NullCollections_AreRejectedBeforeDatabaseAccess()
    {
        var store = new MySqlSpecialtyMarketStore(new RecordingSaveManager());
        SpecialtyMarketState[] invalidStates =
        [
            new() { Revision = 1, PriceRatios = null },
            new() { Revision = 1, DemandRemainders = null },
            new() { Revision = 1, MaterialContributions = null },
            new() { Revision = 1, CargoStock = null },
            new() { Revision = 1, Records = null },
            new() { Revision = 1, StockEventNextChecks = null },
            new() { Revision = 1, StockEventActivations = null }
        ];
        foreach (var invalid in invalidStates)
            Assert.Throws<ArgumentException>(() => store.Commit(new(new(), invalid)));

        Assert.Throws<ArgumentNullException>(() => store.Commit(null));
        Assert.Throws<ArgumentNullException>(() => store.Commit(new(null, new())));
        Assert.Throws<ArgumentNullException>(() => store.Commit(new(new(), null)));
    }

    [Test]
    [Arguments(0L, 0L)]
    [Arguments(0L, 2L)]
    [Arguments(1L, 0L)]
    [Arguments(-1L, 0L)]
    [Arguments(long.MaxValue, long.MinValue)]
    [Arguments(long.MaxValue, long.MaxValue)]
    public void InvalidRevision_IsRejectedBeforeDatabaseAccess(long expectedRevision, long updatedRevision)
    {
        var store = new MySqlSpecialtyMarketStore(new RecordingSaveManager());
        var write = new SpecialtyMarketWrite(new() { Revision = expectedRevision }, new() { Revision = updatedRevision });

        Assert.Throws<ArgumentException>(() => store.Commit(write));
        Assert.Throws<ArgumentException>(() => store.Apply(null, null, write));
    }

    [Test]
    public async Task ValidBoundaryState_ReachesSaveManagerWithoutMutatingInput()
    {
        var expected = new SpecialtyMarketState { Revision = long.MaxValue - 1 };
        var updated = new SpecialtyMarketState
        {
            Revision = long.MaxValue,
            PriceRatios = new() { [uint.MaxValue] = new() { [uint.MaxValue] = int.MaxValue, [1] = 0 } },
            DemandRemainders = new() { [uint.MaxValue] = new() { [uint.MaxValue] = 3 } },
            MaterialContributions = new()
            {
                [(uint.MaxValue, uint.MaxValue)] = [new SpecialtyMaterialContribution(ulong.MaxValue, uint.MaxValue, uint.MaxValue)]
            },
            CargoStock = new() { [(uint.MaxValue, uint.MaxValue)] = 0 },
            Records = new() { [(uint.MaxValue, uint.MaxValue)] = Enumerable.Range(0, 256)
                .Select(i => new SpecialtyMarketRecord(i == 0 ? 0 : int.MaxValue, i == 0 ? 0 : long.MaxValue)).ToList() }
        };
        var write = new SpecialtyMarketWrite(expected, updated);
        var store = new MySqlSpecialtyMarketStore(new RecordingSaveManager());

        // This fake throws on transaction entry, proving validation accepted the state.
        Assert.Throws<NotSupportedException>(() => store.Commit(write));

        await Assert.That(write.Expected).IsSameReferenceAs(expected);
        await Assert.That(write.Updated).IsSameReferenceAs(updated);
        await Assert.That(expected.Revision).IsEqualTo(long.MaxValue - 1);
        await Assert.That(expected.PriceRatios.Count).IsEqualTo(0);
        await Assert.That(updated.Revision).IsEqualTo(long.MaxValue);
        await Assert.That(updated.DemandRemainders[uint.MaxValue].ContainsKey(1)).IsFalse();
        await Assert.That(updated.Records[(uint.MaxValue, uint.MaxValue)].Count).IsEqualTo(256);
    }

    [Test]
    public void Load_TransactionFailure_PropagatesWithoutFallback()
    {
        var store = new MySqlSpecialtyMarketStore(new RecordingSaveManager());

        Assert.Throws<NotSupportedException>(() => store.Load());
    }

    private static SpecialtyMarketState CreateState() => new()
    {
        Revision = 7,
        PriceRatios = new() { [1] = new() { [2] = 100 } },
        DemandRemainders = new() { [1] = new() { [2] = 3 } },
        MaterialContributions = new() { [(2, 3)] = [new SpecialtyMaterialContribution(1, 1, 4)] },
        CargoStock = new() { [(2, 4)] = 5 },
        StockEventNextChecks = new() { [1] = 20 },
        StockEventActivations = new() { [2] = new SpecialtyStockEventActivation(10, 30) },
        Records = new() { [(1, 2)] = [new SpecialtyMarketRecord(100, 10)] }
    };
}
