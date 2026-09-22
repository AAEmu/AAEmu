using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.UnitTests.Game.Models.Game.Faction;

/// <summary>
/// The store contract the manager relies on, run against the in-memory implementation: one row per
/// unordered pair, history that keeps its order, counters keyed by (character, other).
/// </summary>
public class FactionDiplomacyStoreTests
{
    private static readonly DateTime Now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    private static FactionDiplomacyAgreement Agreement(uint f1, uint f2, uint updater = 10) => new()
    {
        Faction1 = f1,
        Faction2 = f2,
        State = RelationState.Neutral,
        NextState = RelationState.Hostile,
        UpdateTime = Now,
        ChangeTime = Now.AddMinutes(60),
        UpdaterId = updater,
        UpdaterName = "Asker",
        ConfirmerId = 20,
        ConfirmerName = "Answerer"
    };

    [Test]
    public async Task Agreements_OneRowPerUnorderedPair()
    {
        var store = new InMemoryFactionDiplomacyStore();

        await Assert.That(store.UpsertAgreement(Agreement(149, 148, updater: 10))).IsTrue();
        await Assert.That(store.UpsertAgreement(Agreement(148, 149, updater: 11))).IsTrue();

        var rows = store.LoadAgreements();
        await Assert.That(rows.Count).IsEqualTo(1);
        await Assert.That(rows[0].UpdaterId).IsEqualTo(11u);
    }

    [Test]
    public async Task Agreements_DeleteWorksInEitherOrder()
    {
        var store = new InMemoryFactionDiplomacyStore();
        store.UpsertAgreement(Agreement(148, 149));

        await Assert.That(store.DeleteAgreement(149, 148)).IsTrue();
        await Assert.That(store.LoadAgreements().Count).IsEqualTo(0);
        await Assert.That(store.DeleteAgreement(148, 149)).IsFalse();
    }

    [Test]
    public async Task Agreements_RejectAnUnsetFaction()
    {
        var store = new InMemoryFactionDiplomacyStore();

        await Assert.That(store.UpsertAgreement(null)).IsFalse();
        await Assert.That(store.UpsertAgreement(Agreement(0, 149))).IsFalse();
        await Assert.That(store.LoadAgreements().Count).IsEqualTo(0);
    }

    [Test]
    public async Task Agreements_LoadReturnsCopies()
    {
        var store = new InMemoryFactionDiplomacyStore();
        store.UpsertAgreement(Agreement(148, 149));

        store.LoadAgreements()[0].UpdaterName = "changed";

        await Assert.That(store.LoadAgreements()[0].UpdaterName).IsEqualTo("Asker");
    }

    [Test]
    public async Task History_KeepsInsertionOrderAndReturnsTheNewestTailOldestFirst()
    {
        var store = new InMemoryFactionDiplomacyStore();
        store.InsertHistory(Agreement(148, 149, updater: 1));
        store.InsertHistory(Agreement(114, 148, updater: 2));
        store.InsertHistory(Agreement(114, 149, updater: 3));

        var tail = store.LoadHistory(2);

        await Assert.That(tail.Count).IsEqualTo(2);
        await Assert.That(tail[0].UpdaterId).IsEqualTo(2u);
        await Assert.That(tail[1].UpdaterId).IsEqualTo(3u);
        await Assert.That(store.LoadHistory(10).Count).IsEqualTo(3);
        await Assert.That(store.LoadHistory(0).Count).IsEqualTo(0);
    }

    [Test]
    public async Task History_RejectsAnUnsetFaction()
    {
        var store = new InMemoryFactionDiplomacyStore();

        await Assert.That(store.InsertHistory(null)).IsFalse();
        await Assert.That(store.InsertHistory(Agreement(148, 0))).IsFalse();
        await Assert.That(store.LoadHistory(5).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Counts_ReplaceByCharacterAndOther()
    {
        var store = new InMemoryFactionDiplomacyStore();

        await Assert.That(store.UpsertCount(new FactionDiplomacyCount(10, 0, 1, Now))).IsTrue();
        await Assert.That(store.UpsertCount(new FactionDiplomacyCount(10, 0, 2, Now.AddMinutes(1)))).IsTrue();
        await Assert.That(store.UpsertCount(new FactionDiplomacyCount(20, 10, 1, Now))).IsTrue();

        var rows = store.LoadCounts();
        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows.Single(r => r.CharacterId == 10 && r.OtherId == 0).Count).IsEqualTo(2u);
        await Assert.That(rows.Single(r => r.CharacterId == 20 && r.OtherId == 10).Count).IsEqualTo(1u);
    }

    [Test]
    public async Task Counts_RejectAnUnsetCharacter()
    {
        var store = new InMemoryFactionDiplomacyStore();

        await Assert.That(store.UpsertCount(new FactionDiplomacyCount(0, 0, 1, Now))).IsFalse();
        await Assert.That(store.LoadCounts().Count).IsEqualTo(0);
    }
}
