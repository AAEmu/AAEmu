using System.Reflection;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;
using Moq;
using MySql.Data.MySqlClient;
using Xunit;

namespace AAEmu.IntegrationTests;

public sealed class FamilyPurchaseRepositoryIntegrationTests(FamilyMySqlFixture fixture) : IClassFixture<FamilyMySqlFixture>
{
    private static readonly DateTime PersistedTime = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExpansionAndRename_CommitRealItemSnapshotsWithFamilyRow()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_FAMILY_TEST_MYSQL to run the isolated MySQL fixture.");
        await using var connection = await fixture.OpenAsync();
        var manager = CreateItemManager();
        var item = CreateRegisteredItem(manager, 7001, 10);
        await SeedFamily(connection, 501);
        SeedItem(manager, connection, item);
        var repository = new MySqlFamilyPurchaseRepository(manager, OpenFixtureConnection);

        Assert.True(WithinGate(() => repository.TryCommitExpansion(501, 0, 1,
            [manager.CapturePersistenceSnapshot(item).WithCount(7)])));
        Assert.Equal(1L, await Scalar(connection, "SELECT increased_member_count FROM families WHERE id=501"));
        Assert.Equal(7L, await Scalar(connection, "SELECT count FROM items WHERE id=7001"));

        item.Count = 7;
        Assert.True(WithinGate(() => repository.TryCommitRename(501, "Old Name", 0, "New Name", 123,
            [manager.CapturePersistenceSnapshot(item).WithCount(6)])));
        Assert.Equal("New Name", await Text(connection, "SELECT name FROM families WHERE id=501"));
        Assert.Equal(123L, await Scalar(connection, "SELECT change_name_time FROM families WHERE id=501"));
        Assert.Equal(6L, await Scalar(connection, "SELECT count FROM items WHERE id=7001"));
    }

    [Fact]
    public async Task FullStackConsumption_DeletesPersistedItemInFamilyTransaction()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_FAMILY_TEST_MYSQL to run the isolated MySQL fixture.");
        await using var connection = await fixture.OpenAsync();
        var manager = CreateItemManager();
        var item = CreateRegisteredItem(manager, 7002, 1);
        await SeedFamily(connection, 502);
        SeedItem(manager, connection, item);
        var repository = new MySqlFamilyPurchaseRepository(manager, OpenFixtureConnection);

        Assert.True(WithinGate(() => repository.TryCommitExpansion(502, 0, 1,
            [manager.CapturePersistenceSnapshot(item).Delete()])));
        Assert.Equal(1L, await Scalar(connection, "SELECT increased_member_count FROM families WHERE id=502"));
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM items WHERE id=7002"));
    }

    [Fact]
    public async Task OptimisticConflictAndStaleItem_RollBackBothRows()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_FAMILY_TEST_MYSQL to run the isolated MySQL fixture.");
        await using var connection = await fixture.OpenAsync();
        var manager = CreateItemManager();
        var item = CreateRegisteredItem(manager, 7003, 10);
        await SeedFamily(connection, 503);
        SeedItem(manager, connection, item);
        var repository = new MySqlFamilyPurchaseRepository(manager, OpenFixtureConnection);
        var snapshot = manager.CapturePersistenceSnapshot(item).WithCount(5);

        Assert.False(WithinGate(() => repository.TryCommitExpansion(503, 9, 10, [snapshot])));
        Assert.Equal(0L, await Scalar(connection, "SELECT increased_member_count FROM families WHERE id=503"));
        Assert.Equal(10L, await Scalar(connection, "SELECT count FROM items WHERE id=7003"));

        item.Count = 9;
        Assert.Throws<InvalidOperationException>(() => WithinGate(() => repository.TryCommitRename(
            503, "Old Name", 0, "Should Roll Back", 456, [snapshot])));
        Assert.Equal("Old Name", await Text(connection, "SELECT name FROM families WHERE id=503"));
        Assert.Equal(0L, await Scalar(connection, "SELECT change_name_time FROM families WHERE id=503"));
        Assert.Equal(10L, await Scalar(connection, "SELECT count FROM items WHERE id=7003"));
    }

    [Fact]
    public async Task Departure_CommitsTheRosterChangeWithTheCertificate()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_FAMILY_TEST_MYSQL to run the isolated MySQL fixture.");
        await using var connection = await fixture.OpenAsync();
        var manager = CreateItemManager();
        var item = CreateRegisteredItem(manager, 7004, 2);
        var family = await SeedFamilyWithMembers(connection, 504, ownerId: 41, memberId: 42);
        SeedItem(manager, connection, item);
        var repository = new MySqlFamilyPurchaseRepository(manager, OpenFixtureConnection);
        family.RemoveMember(family.Members.Single(x => x.Id == 42));
        family.RemovedMemberRejoinUntil = 777;
        family.Exp = 90;

        WithinGate(() =>
        {
            repository.CommitDeparture(family, [manager.CapturePersistenceSnapshot(item).WithCount(1)]);
            return true;
        });

        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM family_members WHERE family_id=504 AND character_id=42"));
        Assert.Equal(1L, await Scalar(connection, "SELECT COUNT(*) FROM family_members WHERE family_id=504 AND character_id=41"));
        Assert.Equal(0L, await Scalar(connection, "SELECT family FROM characters WHERE id=42"));
        Assert.Equal(777L, await Scalar(connection, "SELECT family_rejoin_until FROM characters WHERE id=42"));
        Assert.Equal(90L, await Scalar(connection, "SELECT exp FROM families WHERE id=504"));
        Assert.Equal(1L, await Scalar(connection, "SELECT count FROM items WHERE id=7004"));
    }

    [Fact]
    public async Task Departure_StaleCertificate_RollsBackTheRoster()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_FAMILY_TEST_MYSQL to run the isolated MySQL fixture.");
        await using var connection = await fixture.OpenAsync();
        var manager = CreateItemManager();
        var item = CreateRegisteredItem(manager, 7005, 2);
        var family = await SeedFamilyWithMembers(connection, 505, ownerId: 51, memberId: 52);
        SeedItem(manager, connection, item);
        var repository = new MySqlFamilyPurchaseRepository(manager, OpenFixtureConnection);
        var snapshot = manager.CapturePersistenceSnapshot(item).WithCount(1);
        item.Count = 1;
        family.RemoveMember(family.Members.Single(x => x.Id == 52));
        family.RemovedMemberRejoinUntil = 777;

        Assert.Throws<InvalidOperationException>(() => WithinGate(() =>
        {
            repository.CommitDeparture(family, [snapshot]);
            return true;
        }));

        Assert.Equal(1L, await Scalar(connection, "SELECT COUNT(*) FROM family_members WHERE family_id=505 AND character_id=52"));
        Assert.Equal(505L, await Scalar(connection, "SELECT family FROM characters WHERE id=52"));
        Assert.Equal(0L, await Scalar(connection, "SELECT family_rejoin_until FROM characters WHERE id=52"));
        Assert.Equal(100L, await Scalar(connection, "SELECT exp FROM families WHERE id=505"));
        Assert.Equal(2L, await Scalar(connection, "SELECT count FROM items WHERE id=7005"));
    }

    private MySqlConnection OpenFixtureConnection() { var connection = new MySqlConnection(fixture.ConnectionString); connection.Open(); return connection; }

    private static ItemManager CreateItemManager()
    {
        var manager = new ItemManager(new Mock<ISkillManager>().Object, new Mock<IItemIdManager>().Object,
            new Mock<IContainerIdManager>().Object, new Mock<ILocalizationManager>().Object,
            new Mock<ITaskManager>().Object, new Mock<IWorldManager>().Object);
        SetField(manager, "_allItems", new Dictionary<ulong, Item>());
        SetField(manager, "_removedItems", new List<ulong>());
        SetField(manager, "_allPersistentContainers", new Dictionary<ulong, ItemContainer>());
        SetField(manager, "_itemBagContainers", new Dictionary<ulong, ItemBagContainer>());
        return manager;
    }

    private static Item CreateRegisteredItem(ItemManager manager, ulong id, int count)
    {
        var character = new Character(new UnitCustomModelParams()) { Id = 41_000 };
        var container = new ItemContainer(character.Id, SlotType.Inventory, false, character)
            { Owner = character, ContainerId = id + 10_000, ContainerSize = 50 };
        var item = new Item(id, new ItemTemplate { Id = 48995, MaxCount = 1000 }, count)
        {
            OwnerId = character.Id, SlotType = SlotType.Inventory, Slot = 1, _holdingContainer = container,
            CreateTime = PersistedTime, UnsecureTime = PersistedTime, UnpackTime = PersistedTime,
            ExpirationTime = PersistedTime.AddYears(1), ChargeStartTime = PersistedTime
        };
        container.Items.Add(item);
        container.UpdateFreeSlotCount();
        Assert.True(manager.AddItem(item));
        return item;
    }

    private static void SeedItem(ItemManager manager, MySqlConnection connection, Item item) => WithinGate(() =>
    {
        using var transaction = connection.BeginTransaction();
        manager.PersistSnapshots(connection, transaction, [manager.CapturePersistenceSnapshot(item)]);
        transaction.Commit();
        return true;
    });

    private static async Task<Family> SeedFamilyWithMembers(MySqlConnection connection, uint familyId,
        uint ownerId, uint memberId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO families(id,name,exp) VALUES(@family_id,'Old Name',100);" +
            "INSERT INTO characters(id,family) VALUES(@owner_id,@family_id),(@member_id,@family_id);" +
            "INSERT INTO family_members(character_id,family_id,name,role,title) " +
            "VALUES(@owner_id,@family_id,'Owner',1,''),(@member_id,@family_id,'Member',0,'')";
        command.Parameters.AddWithValue("@family_id", familyId);
        command.Parameters.AddWithValue("@owner_id", ownerId);
        command.Parameters.AddWithValue("@member_id", memberId);
        await command.ExecuteNonQueryAsync();

        var family = new Family { Id = familyId, Name = "Old Name", Exp = 100 };
        family.AddMember(new FamilyMember { Id = ownerId, Name = "Owner", Role = 1, Title = "" });
        family.AddMember(new FamilyMember { Id = memberId, Name = "Member", Role = 0, Title = "" });
        return family;
    }

    private static async Task SeedFamily(MySqlConnection connection, uint familyId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO families(id,name) VALUES(@family_id,'Old Name')";
        command.Parameters.AddWithValue("@family_id", familyId);
        await command.ExecuteNonQueryAsync();
    }

    private static T WithinGate<T>(Func<T> action) { PersistenceGate.EnterOperation(); try { return action(); } finally { PersistenceGate.ExitOperation(); } }
    private static async Task<long> Scalar(MySqlConnection connection, string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(await command.ExecuteScalarAsync()); }
    private static async Task<string> Text(MySqlConnection connection, string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToString(await command.ExecuteScalarAsync()); }
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
