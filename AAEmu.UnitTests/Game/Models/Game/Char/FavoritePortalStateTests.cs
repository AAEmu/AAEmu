using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class FavoritePortalStateTests
{
    private static bool Owns(FavoritePortalRef favorite) =>
        favorite is { PortalType: (byte)PortalBookType.Private, PortalId: 1 or 2 } ||
        favorite is { PortalType: (byte)PortalBookType.Return, PortalId: 10 or 11 };

    [Test]
    public async Task Apply_AddsRemovesAndPreservesInsertionOrder()
    {
        var state = FavoritePortalState.Empty;
        await Assert.That(state.TryApply(
        [
            new FavoritePortalChange((byte)PortalBookType.Private, 1, true),
            new FavoritePortalChange((byte)PortalBookType.Return, 10, true),
            new FavoritePortalChange((byte)PortalBookType.Private, 2, true)
        ], Owns, int.MaxValue, out state)).IsTrue();

        await Assert.That(state.Favorites).IsEquivalentTo(new[]
        {
            new FavoritePortalRef((byte)PortalBookType.Private, 1),
            new FavoritePortalRef((byte)PortalBookType.Return, 10),
            new FavoritePortalRef((byte)PortalBookType.Private, 2)
        });

        await Assert.That(state.TryApply(
            [new FavoritePortalChange((byte)PortalBookType.Return, 10, false)],
            Owns,
            int.MaxValue,
            out state)).IsTrue();
        await Assert.That(state.Favorites).IsEquivalentTo(new[]
        {
            new FavoritePortalRef((byte)PortalBookType.Private, 1),
            new FavoritePortalRef((byte)PortalBookType.Private, 2)
        });
    }

    [Test]
    public async Task Apply_IsAtomicForUnknownAdditionsAndDuplicateBatchEntries()
    {
        var state = FavoritePortalState.Empty;
        await Assert.That(state.TryApply(
            [new FavoritePortalChange((byte)PortalBookType.Private, 1, true)],
            Owns,
            int.MaxValue,
            out state)).IsTrue();

        await Assert.That(state.TryApply(
        [
            new FavoritePortalChange((byte)PortalBookType.Private, 2, true),
            new FavoritePortalChange((byte)PortalBookType.Private, 99, true)
        ], Owns, int.MaxValue, out var rejected)).IsFalse();
        await Assert.That(ReferenceEquals(rejected, state)).IsTrue();

        await Assert.That(state.TryApply(
        [
            new FavoritePortalChange((byte)PortalBookType.Private, 2, true),
            new FavoritePortalChange((byte)PortalBookType.Private, 2, false)
        ], Owns, int.MaxValue, out rejected)).IsFalse();
        await Assert.That(ReferenceEquals(rejected, state)).IsTrue();
    }

    [Test]
    public async Task Apply_TreatsRemovalOfMissingEntryAsIdempotent()
    {
        var state = FavoritePortalState.Empty;
        await Assert.That(state.TryApply(
            [new FavoritePortalChange((byte)PortalBookType.Return, 99, false)],
            Owns,
            int.MaxValue,
            out state)).IsTrue();
        await Assert.That(state.Favorites).IsEmpty();
    }

    [Test]
    public async Task Apply_RejectsWholeBatchWhenCapacityWouldBeExceeded()
    {
        var state = FavoritePortalState.Empty;
        await Assert.That(state.TryApply(
            [new FavoritePortalChange((byte)PortalBookType.Private, 1, true)],
            Owns,
            1,
            out state)).IsTrue();

        var accepted = state.TryApply(
        [
            new FavoritePortalChange((byte)PortalBookType.Private, 1, true),
            new FavoritePortalChange((byte)PortalBookType.Private, 2, true)
        ], Owns, 1, out var rejected);

        await Assert.That(accepted).IsFalse();
        await Assert.That(ReferenceEquals(rejected, state)).IsTrue();
        await Assert.That(state.Favorites).IsEquivalentTo(new[]
        {
            new FavoritePortalRef((byte)PortalBookType.Private, 1)
        });
    }

    [Test]
    public async Task BuildFlaggedPortals_PreservesPortalOrderAndSetsFlags()
    {
        var state = FavoritePortalState.Empty;
        await Assert.That(state.TryApply(
        [
            new FavoritePortalChange((byte)PortalBookType.Private, 2, true),
            new FavoritePortalChange((byte)PortalBookType.Private, 1, true)
        ], Owns, int.MaxValue, out state)).IsTrue();

        var privatePortals = new[]
        {
            new Portal { Id = 1, Name = "one" },
            new Portal { Id = 2, Name = "two" },
            new Portal { Id = 3, Name = "three", IsFavorite = true }
        };
        var flagged = state.BuildFlaggedPortals(privatePortals, PortalBookType.Private);

        await Assert.That(flagged.Select(portal => portal.Id)).IsEquivalentTo(new uint[] { 1, 2, 3 });
        await Assert.That(flagged[0].IsFavorite).IsTrue();
        await Assert.That(flagged[1].IsFavorite).IsTrue();
        await Assert.That(flagged[2].IsFavorite).IsFalse();
    }
}
