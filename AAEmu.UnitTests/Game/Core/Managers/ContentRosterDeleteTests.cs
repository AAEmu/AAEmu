using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class ContentRosterDeleteTests
{
    private static readonly DateTime BaseTime = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeContentRosterStore : IContentRosterStore
    {
        private readonly Dictionary<ulong, ulong> _ownerByRosterId = new();

        public void Seed(ulong rosterId, ulong accountId) => _ownerByRosterId[rosterId] = accountId;

        public bool Contains(ulong rosterId) => _ownerByRosterId.ContainsKey(rosterId);

        public IReadOnlySet<ulong> QueryExisting(IReadOnlyList<ulong> rosterIds) =>
            rosterIds.Where(_ownerByRosterId.ContainsKey).ToHashSet();

        public IReadOnlySet<ulong> QueryOwned(ulong accountId, IReadOnlyList<ulong> rosterIds) =>
            rosterIds
                .Where(id => _ownerByRosterId.TryGetValue(id, out var owner) && owner == accountId)
                .ToHashSet();

        public int DeleteOwned(ulong accountId, IReadOnlyList<ulong> rosterIds)
        {
            var removed = 0;
            foreach (var id in rosterIds.Distinct())
            {
                if (_ownerByRosterId.TryGetValue(id, out var owner) && owner == accountId &&
                    _ownerByRosterId.Remove(id))
                {
                    removed++;
                }
            }

            return removed;
        }

        public ulong Insert(ulong accountId, string title, DateTime createdAt, IReadOnlyList<uint> memberCharacterIds)
        {
            var id = _ownerByRosterId.Count == 0 ? 1UL : _ownerByRosterId.Keys.Max() + 1;
            _ownerByRosterId[id] = accountId;
            return id;
        }

        public IReadOnlyList<ContentRosterHeader> List(ulong accountId) => [];
    }

    [Test]
    public async Task Delete_OwnedRoster_PersistsRemovalAndReturnsSuccess()
    {
        var store = new FakeContentRosterStore();
        store.Seed(42, 7);
        var service = new ContentRosterService(store);

        var outcome = service.Delete(7, [42], BaseTime);

        await Assert.That(outcome.Result).IsEqualTo(ContentRosterDeleteResult.Success);
        await Assert.That(outcome.DeletedCount).IsEqualTo(1);
        await Assert.That(store.Contains(42)).IsFalse();
    }

    [Test]
    public async Task Delete_RosterOwnedByAnotherAccount_RefusedDefinitively()
    {
        var store = new FakeContentRosterStore();
        store.Seed(42, 9);
        var service = new ContentRosterService(store);

        var outcome = service.Delete(7, [42], BaseTime);

        await Assert.That(outcome.Result).IsEqualTo(ContentRosterDeleteResult.NotOwner);
        await Assert.That(outcome.DeletedCount).IsEqualTo(0);
        await Assert.That(store.Contains(42)).IsTrue();
    }

    [Test]
    public async Task Delete_UnknownRoster_RefusedDefinitively()
    {
        var store = new FakeContentRosterStore();
        var service = new ContentRosterService(store);

        var outcome = service.Delete(7, [99], BaseTime);

        await Assert.That(outcome.Result).IsEqualTo(ContentRosterDeleteResult.UnknownRoster);
        await Assert.That(outcome.DeletedCount).IsEqualTo(0);
    }

    [Test]
    public async Task Delete_EmptyRequest_RefusedDefinitively()
    {
        var store = new FakeContentRosterStore();
        var service = new ContentRosterService(store);

        var outcome = service.Delete(7, [], BaseTime);

        await Assert.That(outcome.Result).IsEqualTo(ContentRosterDeleteResult.InvalidRequest);
    }

    [Test]
    public async Task Delete_IsNotGatedByTheSaveCooldown()
    {
        var store = new FakeContentRosterStore();
        store.Seed(1, 7);
        store.Seed(2, 7);
        var service = new ContentRosterService(store);

        var first = service.Delete(7, [1], BaseTime);
        var immediately = service.Delete(7, [2], BaseTime);

        await Assert.That(first.Result).IsEqualTo(ContentRosterDeleteResult.Success);
        await Assert.That(immediately.Result).IsEqualTo(ContentRosterDeleteResult.Success);
        await Assert.That(store.Contains(2)).IsFalse();
    }
}
