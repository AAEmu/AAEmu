using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

/// <summary>
/// A hero agreement overlaid on the loaded relation table changes what GetRelationState answers
/// for the two nations, and clearing it puts the content row back. Race factions resolve through
/// their mother (SystemFaction.GetRelationState via FactionManager.Instance), which needs DI, so
/// only the nation pair is asserted here.
/// </summary>
public class FactionManagerDiplomacyTests
{
    private const uint Nuia = 148;
    private const uint Haranya = 149;

    private static readonly DateTime Now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    private static FactionManager Seeded()
    {
        var manager = new FactionManager(null);
        manager.AddFaction(new SystemFaction { Id = (FactionsEnum)Nuia });
        manager.AddFaction(new SystemFaction { Id = (FactionsEnum)Haranya });
        // system_faction_relations row (148, 149, state_id 1).
        manager.AddRelation(new FactionRelation { Id = (FactionsEnum)Nuia, Id2 = (FactionsEnum)Haranya, State = RelationState.Hostile });
        return manager;
    }

    private static FactionDiplomacyAgreement Agreement(uint f1, uint f2) => new()
    {
        Faction1 = f1,
        Faction2 = f2,
        State = RelationState.Neutral,
        NextState = RelationState.Hostile,
        UpdateTime = Now,
        ChangeTime = Now.AddMinutes(60),
        UpdaterId = 10,
        UpdaterName = "Asker",
        ConfirmerId = 20,
        ConfirmerName = "Answerer"
    };

    [Test]
    public async Task Apply_ChangesTheNationRelationBothWays()
    {
        var manager = Seeded();
        var nuia = manager.GetFaction((FactionsEnum)Nuia);
        var haranya = manager.GetFaction((FactionsEnum)Haranya);

        await Assert.That(nuia.GetRelationState(haranya)).IsEqualTo(RelationState.Hostile);

        manager.ApplyDiplomacy(Agreement(Nuia, Haranya));

        await Assert.That(nuia.GetRelationState(haranya)).IsEqualTo(RelationState.Neutral);
        await Assert.That(haranya.GetRelationState(nuia)).IsEqualTo(RelationState.Neutral);
    }

    [Test]
    public async Task Apply_FillsTheWireFieldsOnTheLoadedRow()
    {
        var manager = Seeded();

        var row = manager.ApplyDiplomacy(Agreement(Nuia, Haranya));

        await Assert.That(row.HasDiplomacy).IsTrue();
        await Assert.That(row.NextState).IsEqualTo(RelationState.Hostile);
        await Assert.That(row.ChangeTime).IsEqualTo(Now.AddMinutes(60));
        await Assert.That(row.UpdaterName).IsEqualTo("Asker");
        await Assert.That(row.ConfirmerId).IsEqualTo(20u);
        await Assert.That(manager.GetRelation((FactionsEnum)Haranya, (FactionsEnum)Nuia)).IsSameReferenceAs(row);
    }

    [Test]
    public async Task Clear_PutsTheContentStateBackAndZeroesTheOverlay()
    {
        var manager = Seeded();
        var nuia = manager.GetFaction((FactionsEnum)Nuia);
        var haranya = manager.GetFaction((FactionsEnum)Haranya);
        manager.ApplyDiplomacy(Agreement(Nuia, Haranya));

        var row = manager.ClearDiplomacy(Nuia, Haranya);

        await Assert.That(nuia.GetRelationState(haranya)).IsEqualTo(RelationState.Hostile);
        await Assert.That(row.HasDiplomacy).IsFalse();
        await Assert.That(row.UpdaterName).IsEqualTo(string.Empty);
        await Assert.That(row.ChangeTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task Apply_TwiceKeepsTheOriginalContentState()
    {
        var manager = Seeded();
        var nuia = manager.GetFaction((FactionsEnum)Nuia);
        var haranya = manager.GetFaction((FactionsEnum)Haranya);
        manager.ApplyDiplomacy(Agreement(Nuia, Haranya));
        manager.ApplyDiplomacy(Agreement(Nuia, Haranya));

        manager.ClearDiplomacy(Nuia, Haranya);

        await Assert.That(nuia.GetRelationState(haranya)).IsEqualTo(RelationState.Hostile);
    }

    [Test]
    public async Task Apply_OnAPairWithoutAContentRowAddsOneThatClearRemoves()
    {
        var manager = new FactionManager(null);
        manager.AddFaction(new SystemFaction { Id = (FactionsEnum)Nuia });
        manager.AddFaction(new SystemFaction { Id = (FactionsEnum)Haranya });

        manager.ApplyDiplomacy(Agreement(Nuia, Haranya));
        await Assert.That(manager.GetRelation((FactionsEnum)Nuia, (FactionsEnum)Haranya)).IsNotNull();
        await Assert.That(manager.GetFaction((FactionsEnum)Nuia).Relations.ContainsKey((FactionsEnum)Haranya)).IsTrue();

        manager.ClearDiplomacy(Nuia, Haranya);

        await Assert.That(manager.GetRelation((FactionsEnum)Nuia, (FactionsEnum)Haranya)).IsNull();
        await Assert.That(manager.GetFaction((FactionsEnum)Nuia).Relations.ContainsKey((FactionsEnum)Haranya)).IsFalse();
    }

    [Test]
    public async Task Clear_OnAPairWithoutAnOverlayIsNeutral()
    {
        var manager = Seeded();
        var nuia = manager.GetFaction((FactionsEnum)Nuia);
        var haranya = manager.GetFaction((FactionsEnum)Haranya);

        var row = manager.ClearDiplomacy(Nuia, Haranya);

        await Assert.That(row).IsNotNull();
        await Assert.That(nuia.GetRelationState(haranya)).IsEqualTo(RelationState.Hostile);
        await Assert.That(manager.ClearDiplomacy(1, 2)).IsNull();
    }

    [Test]
    public async Task ContentZoneRelations_SendTheOverlaidPairInItsContentState()
    {
        var manager = Seeded();
        manager.ApplyDiplomacy(Agreement(Nuia, Haranya));

        var zoneRows = manager.GetZoneRelations();
        var contentRows = manager.GetContentZoneRelations();

        await Assert.That(zoneRows.Count).IsEqualTo(1);
        await Assert.That(zoneRows[0].State).IsEqualTo(RelationState.Neutral);
        await Assert.That(contentRows.Count).IsEqualTo(1);
        await Assert.That(contentRows[0].State).IsEqualTo(RelationState.Hostile);
        await Assert.That(contentRows[0].HasDiplomacy).IsFalse();
    }

    [Test]
    public async Task ContentZoneRelations_DropAPairContentHasNoRowFor()
    {
        var manager = new FactionManager(null);
        manager.AddFaction(new SystemFaction { Id = (FactionsEnum)Nuia });
        manager.AddFaction(new SystemFaction { Id = (FactionsEnum)Haranya });
        manager.ApplyDiplomacy(Agreement(Nuia, Haranya));

        await Assert.That(manager.GetZoneRelations().Count).IsEqualTo(1);
        await Assert.That(manager.GetContentZoneRelations().Count).IsEqualTo(0);
    }

    [Test]
    public async Task CharacterDeletionDropsOnlyThatCharactersCachedCounters()
    {
        var store = new InMemoryFactionDiplomacyStore();
        store.UpsertCount(new FactionDiplomacyCount(42, 99, 2, Now));
        store.UpsertCount(new FactionDiplomacyCount(99, 42, 3, Now));
        store.UpsertCount(new FactionDiplomacyCount(99, 100, 1, Now));
        var diplomacy = new FactionDiplomacyManager(Seeded(), Mock.Of<ITaskManager>().Object);
        diplomacy.UseStore(store);
        diplomacy.LoadFromStore(Now);

        diplomacy.OnCharacterDeleted(42);

        await Assert.That(diplomacy.HasCountFor(42)).IsFalse();
        await Assert.That(diplomacy.HasCountFor(99)).IsTrue();
    }
}
