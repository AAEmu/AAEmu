using System.Runtime.CompilerServices;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Account;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Units;
using Moq;
using MySql.Data.MySqlClient;
using Xunit;

namespace AAEmu.IntegrationTests;

public sealed class ButlerPersistenceIntegrationTests(ButlerMySqlFixture fixture) : IClassFixture<ButlerMySqlFixture>
{
    private static readonly DateTime PersistedTime = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Repository_CasAndCompletionMarker_AreDurableAndIdempotent()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_BUTLER_TEST_MYSQL to run the isolated MySQL fixture.");
        await using var connection = await fixture.OpenAsync();
        var repository = new MySqlButlerRepository();
        await using var transaction = await connection.BeginTransactionAsync();
        var record = new CharacterButlerRecord(101, 201, "Fixture", 30, 0, 12);
        Assert.True(repository.TryChangeHouse(record, 0, connection, transaction));
        var job = new ButlerHarvestJobCandidate(1, 2, 3, 4, 5);
        var jobId = repository.InsertHarvestJob(101, job, connection, transaction);
        Assert.True(repository.TryInsertHarvestCompletion(jobId, 1, 6, connection, transaction));
        Assert.False(repository.TryInsertHarvestCompletion(jobId, 1, 6, connection, transaction));
        await transaction.CommitAsync();
        await using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT COUNT(*) FROM character_butler_harvest_completions WHERE job_id=@id";
        verify.Parameters.AddWithValue("@id", jobId);
        Assert.Equal(1L, Convert.ToInt64(await verify.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task AccountLaborDebit_CommitAndRollback_RespectExpectedBalances()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_BUTLER_TEST_MYSQL to run the isolated MySQL fixture.");
        await using var connection = await fixture.OpenAsync();
        await using (var seed = connection.CreateCommand()) { seed.CommandText = "INSERT INTO accounts VALUES(1,10,8)"; await seed.ExecuteNonQueryAsync(); }
        var manager = new AccountManager(new Mock<ITickManager>().Object, new Mock<ITimedRewardsManager>().Object);
        Assert.True(AccountLaborDebitRules.TryCreate(1, new(10, 8), 12, out var debit));
        manager.WithAccountLock(1, () => { using var tx = connection.BeginTransaction(); Assert.True(manager.TryDebitLaborOn(debit, connection, tx)); tx.Rollback(); return 0; });
        await using (var read = connection.CreateCommand()) { read.CommandText = "SELECT labor FROM accounts WHERE account_id=1"; Assert.Equal(10, Convert.ToInt32(await read.ExecuteScalarAsync())); }
        manager.WithAccountLock(1, () => { using var tx = connection.BeginTransaction(); Assert.True(manager.TryDebitLaborOn(debit, connection, tx)); tx.Commit(); return 0; });
        await using var verify = connection.CreateCommand(); verify.CommandText = "SELECT local_labor FROM accounts WHERE account_id=1"; Assert.Equal(6, Convert.ToInt32(await verify.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task ItemSnapshots_CountDeleteAndMove_CommitAndRollbackWithoutLeakingLiveState()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_BUTLER_TEST_MYSQL to run the isolated MySQL fixture.");

        const uint characterId = 41_001;
        const ulong countItemId = 0x0100_1001;
        const ulong deleteItemId = 0x0100_1002;
        const ulong moveItemId = 0x0100_1003;
        await using var connection = await fixture.OpenAsync();
        var itemIds = new Mock<IItemIdManager>();
        var manager = CreateItemManager(itemIds);
        var state = CreateInventoryState(
            manager,
            characterId,
            sourceContainerId: 0x0001_1001,
            destinationContainerId: 0x0001_1002,
            (countItemId, 31_001u, 9, 1),
            (deleteItemId, 31_002u, 1, 2),
            (moveItemId, 31_003u, 3, 3));
        var countItem = state.Items[0];
        var deleteItem = state.Items[1];
        var moveItem = state.Items[2];
        SeedCurrentItems(manager, state.Inventory, connection, state.Items);

        PersistenceGate.EnterOperation();
        try
        {
            using (AcquireFarmhandMutation(state.Inventory))
            {
                Assert.True(Monitor.IsEntered(state.Inventory.MutationSyncRoot));
                var snapshots = new[]
                {
                    manager.CapturePersistenceSnapshot(countItem).WithCount(4),
                    manager.CapturePersistenceSnapshot(deleteItem).Delete(),
                    manager.CapturePersistenceSnapshot(moveItem).MoveTo(state.Bag, 7)
                };
                using var transaction = connection.BeginTransaction();
                Assert.Equal(3, manager.PersistSnapshots(connection, transaction, snapshots));
                transaction.Rollback();
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        Assert.Same(countItem, manager.GetItemByItemId(countItemId));
        Assert.Same(deleteItem, manager.GetItemByItemId(deleteItemId));
        Assert.Same(moveItem, manager.GetItemByItemId(moveItemId));
        Assert.Equal(9, countItem.Count);
        Assert.Same(state.System, moveItem._holdingContainer);
        Assert.Equal(SlotType.System, moveItem.SlotType);
        Assert.Contains(deleteItem, state.System.Items);
        Assert.Empty(state.Bag.Items);
        itemIds.Verify(ids => ids.ReleaseId((uint)deleteItemId), Times.Never);

        var rolledBackCount = await ReadItemAsync(connection, countItemId);
        var rolledBackDelete = await ReadItemAsync(connection, deleteItemId);
        var rolledBackMove = await ReadItemAsync(connection, moveItemId);
        Assert.Equal(9, rolledBackCount.Count);
        Assert.NotNull(rolledBackDelete);
        Assert.Equal(state.System.ContainerId, rolledBackMove.ContainerId);
        Assert.Equal(SlotType.System, rolledBackMove.SlotType);
        Assert.Equal(3, rolledBackMove.Slot);

        PersistenceGate.EnterOperation();
        try
        {
            using (AcquireFarmhandMutation(state.Inventory))
            {
                var countSnapshot = manager.CapturePersistenceSnapshot(countItem).WithCount(4);
                var deleteSnapshot = manager.CapturePersistenceSnapshot(deleteItem).Delete();
                var moveSnapshot = manager.CapturePersistenceSnapshot(moveItem).MoveTo(state.Bag, 7);
                using var transaction = connection.BeginTransaction();
                Assert.Equal(3, manager.PersistSnapshots(
                    connection,
                    transaction,
                    [countSnapshot, deleteSnapshot, moveSnapshot]));
                transaction.Commit();
                manager.ApplyCommittedSnapshot(countSnapshot);
                manager.FinalizeCommittedRemoval(deleteItem);
                manager.ApplyCommittedSnapshot(moveSnapshot);
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        var committedCount = await ReadItemAsync(connection, countItemId);
        var committedDelete = await ReadItemAsync(connection, deleteItemId);
        var committedMove = await ReadItemAsync(connection, moveItemId);
        Assert.Equal(4, committedCount.Count);
        Assert.Null(committedDelete);
        Assert.Equal(state.Bag.ContainerId, committedMove.ContainerId);
        Assert.Equal(SlotType.Inventory, committedMove.SlotType);
        Assert.Equal(7, committedMove.Slot);
        Assert.Equal((ulong)characterId, committedMove.OwnerId);

        Assert.Same(countItem, manager.GetItemByItemId(countItemId));
        Assert.Null(manager.GetItemByItemId(deleteItemId));
        Assert.Same(moveItem, manager.GetItemByItemId(moveItemId));
        Assert.Same(state.Bag, manager.GetItemContainerByDbId(state.Bag.ContainerId));
        Assert.Equal(4, countItem.Count);
        Assert.DoesNotContain(deleteItem, state.System.Items);
        Assert.DoesNotContain(moveItem, state.System.Items);
        Assert.Contains(moveItem, state.Bag.Items);
        Assert.Same(state.Bag, moveItem._holdingContainer);
        itemIds.Verify(ids => ids.ReleaseId((uint)deleteItemId), Times.Once);
    }

    [Fact]
    public async Task ExistingItemMailPlan_SharesButlerTransactionAndPreservesAttachmentIdentity()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_BUTLER_TEST_MYSQL to run the isolated MySQL fixture.");

        const uint characterId = 41_002;
        const uint houseId = 51_002;
        const ulong itemId = 0x0100_2001;
        const uint firstMailId = 20_000;
        await using var connection = await fixture.OpenAsync();
        var itemIds = new Mock<IItemIdManager>();
        var itemManager = CreateItemManager(itemIds);
        var state = CreateInventoryState(
            itemManager,
            characterId,
            sourceContainerId: 0x0001_2001,
            destinationContainerId: 0x0001_2002,
            (itemId, 32_001u, 1, 4));
        var item = state.Items[0];
        SeedCurrentItems(itemManager, state.Inventory, connection, state.Items);

        var mailIds = new Mock<IMailIdManager>();
        var nextMailId = firstMailId;
        mailIds.Setup(ids => ids.GetNextId()).Returns(() => nextMailId++);
        var mailManager = CreateMailManager(mailIds, itemManager, characterId);
        var repository = new MySqlButlerRepository();
        var butler = new CharacterButlerRecord(characterId, houseId, "Transaction fixture", 30, 0, 0);
        BaseMail rolledBackMail;

        PersistenceGate.EnterOperation();
        try
        {
            using (AcquireFarmhandMutation(state.Inventory))
            {
                Assert.True(mailManager.TryCreateExistingItemDeliveryPlan(
                    [item],
                    (_, _) => CreateMail(characterId),
                    out var plan));
                using (plan)
                {
                    rolledBackMail = Assert.Single(plan.Mails);
                    Assert.Equal((long)firstMailId, rolledBackMail.Id);
                    Assert.Same(item, Assert.Single(rolledBackMail.Body.Attachments));
                    using var transaction = connection.BeginTransaction();
                    Assert.True(repository.TryChangeHouse(butler, 0, connection, transaction));
                    Assert.True(plan.TryPersistOn(connection, transaction));
                    Assert.Equal(1L, ScalarOn(
                        connection,
                        transaction,
                        "SELECT COUNT(*) FROM character_butlers WHERE character_id=@id",
                        ("@id", characterId)));
                    Assert.Equal((long)itemId, ScalarOn(
                        connection,
                        transaction,
                        "SELECT attachment0 FROM mails WHERE id=@id",
                        ("@id", firstMailId)));
                    Assert.Equal((long)SlotType.Mail, ScalarOn(
                        connection,
                        transaction,
                        "SELECT slot_type FROM items WHERE id=@id",
                        ("@id", itemId)));
                    transaction.Rollback();
                    plan.Rollback();
                }
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        Assert.Equal(0L, rolledBackMail.Id);
        Assert.Empty(rolledBackMail.Body.Attachments);
        Assert.Null(mailManager.GetMailById(firstMailId));
        Assert.Same(item, itemManager.GetItemByItemId(itemId));
        Assert.Equal(itemId, item.Id);
        Assert.Same(state.System, item._holdingContainer);
        Assert.Equal(SlotType.System, item.SlotType);
        Assert.Contains(item, state.System.Items);
        mailIds.Verify(ids => ids.ReleaseId(firstMailId), Times.Once);
        itemIds.Verify(ids => ids.ReleaseId((uint)itemId), Times.Never);
        Assert.Equal(0L, await CountAsync(connection, "character_butlers", "character_id", characterId));
        Assert.Equal(0L, await CountAsync(connection, "mails", "id", firstMailId));
        var rolledBackItem = await ReadItemAsync(connection, itemId);
        Assert.Equal(state.System.ContainerId, rolledBackItem.ContainerId);
        Assert.Equal(SlotType.System, rolledBackItem.SlotType);

        BaseMail committedMail;
        var committedMailId = firstMailId + 1;
        PersistenceGate.EnterOperation();
        try
        {
            using (AcquireFarmhandMutation(state.Inventory))
            {
                Assert.True(mailManager.TryCreateExistingItemDeliveryPlan(
                    [item],
                    (_, _) => CreateMail(characterId),
                    out var plan));
                using (plan)
                {
                    committedMail = Assert.Single(plan.Mails);
                    Assert.Equal((long)committedMailId, committedMail.Id);
                    Assert.Same(item, Assert.Single(committedMail.Body.Attachments));
                    using var transaction = connection.BeginTransaction();
                    Assert.True(repository.TryChangeHouse(butler, 0, connection, transaction));
                    Assert.True(plan.TryPersistOn(connection, transaction));
                    transaction.Commit();
                    plan.Commit();
                }
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }

        Assert.Same(committedMail, mailManager.GetMailById(committedMailId));
        Assert.False(committedMail.IsPendingPublish);
        Assert.Same(item, Assert.Single(committedMail.Body.Attachments));
        Assert.Same(item, itemManager.GetItemByItemId(itemId));
        Assert.Equal(itemId, item.Id);
        Assert.Null(item._holdingContainer);
        Assert.Equal(SlotType.Mail, item.SlotType);
        Assert.Equal(0, item.Slot);
        Assert.Equal((ulong)characterId, item.OwnerId);
        Assert.DoesNotContain(item, state.System.Items);

        Assert.Equal(1L, await CountAsync(connection, "character_butlers", "character_id", characterId));
        Assert.Equal(1L, await CountAsync(connection, "mails", "id", committedMailId));
        var committedItem = await ReadItemAsync(connection, itemId);
        Assert.Equal(0UL, committedItem.ContainerId);
        Assert.Equal(SlotType.Mail, committedItem.SlotType);
        Assert.Equal(0, committedItem.Slot);
        Assert.Equal((ulong)characterId, committedItem.OwnerId);
        await using (var attachment = connection.CreateCommand())
        {
            attachment.CommandText = "SELECT attachment0 FROM mails WHERE id=@id";
            attachment.Parameters.AddWithValue("@id", committedMailId);
            Assert.Equal((long)itemId, Convert.ToInt64(await attachment.ExecuteScalarAsync()));
        }
        mailIds.Verify(ids => ids.ReleaseId(firstMailId), Times.Once);
        mailIds.Verify(ids => ids.ReleaseId(committedMailId), Times.Never);
        itemIds.Verify(ids => ids.ReleaseId((uint)itemId), Times.Never);
    }

    private static ItemManager CreateItemManager(Mock<IItemIdManager> itemIds)
    {
        var manager = new ItemManager(
            new Mock<ISkillManager>().Object,
            itemIds.Object,
            new Mock<IContainerIdManager>().Object,
            new Mock<ILocalizationManager>().Object,
            new Mock<ITaskManager>().Object,
            new Mock<IWorldManager>().Object);
        SetPrivateField(manager, "_allItems", new Dictionary<ulong, Item>());
        SetPrivateField(manager, "_removedItems", new List<ulong>());
        SetPrivateField(manager, "_allPersistentContainers", new Dictionary<ulong, ItemContainer>());
        SetPrivateField(manager, "_itemBagContainers", new Dictionary<ulong, ItemBagContainer>());
        return manager;
    }

    private static MailManager CreateMailManager(
        Mock<IMailIdManager> mailIds,
        ItemManager itemManager,
        uint receiverId)
    {
        const string receiverName = "Transaction Receiver";
        var names = new Mock<INameManager>();
        names.Setup(manager => manager.GetCharacterName(receiverId)).Returns(receiverName);
        names.Setup(manager => manager.GetCharacterId(receiverName)).Returns(receiverId);
        return new MailManager(
            mailIds.Object,
            names.Object,
            itemManager,
            new Mock<ITaskManager>().Object,
            new Mock<IWorldManager>().Object,
            new Lazy<IHousingManager>(() => new Mock<IHousingManager>().Object),
            new Mock<ILocalizationManager>().Object);
    }

    private static InventoryState CreateInventoryState(
        ItemManager manager,
        uint characterId,
        ulong sourceContainerId,
        ulong destinationContainerId,
        params (ulong Id, uint TemplateId, int Count, int Slot)[] definitions)
    {
        var character = new Character(new UnitCustomModelParams()) { Id = characterId };
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        typeof(Inventory).GetField(nameof(Inventory.Owner))!.SetValue(inventory, character);
        var system = new ItemContainer(characterId, SlotType.System, false, character)
        {
            Owner = character,
            ContainerId = sourceContainerId,
            ContainerSize = -1
        };
        var bag = new ItemContainer(characterId, SlotType.Inventory, false, character)
        {
            Owner = character,
            ContainerId = destinationContainerId,
            ContainerSize = 50
        };
        typeof(Inventory).GetProperty(nameof(Inventory.SystemContainer))!.SetValue(inventory, system);
        typeof(Inventory).GetProperty(nameof(Inventory.Bag))!.SetValue(inventory, bag);
        typeof(Inventory).GetProperty(nameof(Inventory._itemContainers))!.SetValue(
            inventory,
            new Dictionary<SlotType, ItemContainer>
            {
                [SlotType.System] = system,
                [SlotType.Inventory] = bag
            });
        character.Inventory = inventory;

        var containers = GetPrivateField<Dictionary<ulong, ItemContainer>>(manager, "_allPersistentContainers");
        containers.Add(system.ContainerId, system);
        containers.Add(bag.ContainerId, bag);
        var items = definitions.Select(definition =>
        {
            var item = new Item(
                definition.Id,
                new ItemTemplate { Id = definition.TemplateId, MaxCount = 1_000 },
                definition.Count)
            {
                OwnerId = characterId,
                SlotType = SlotType.System,
                Slot = definition.Slot,
                _holdingContainer = system,
                CreateTime = PersistedTime,
                UnsecureTime = PersistedTime,
                UnpackTime = PersistedTime,
                ExpirationTime = PersistedTime.AddYears(1),
                ChargeStartTime = PersistedTime
            };
            system.Items.Add(item);
            Assert.True(manager.AddItem(item));
            return item;
        }).ToArray();
        system.UpdateFreeSlotCount();
        bag.UpdateFreeSlotCount();
        return new InventoryState(character, inventory, system, bag, items);
    }

    private static void SeedCurrentItems(
        ItemManager manager,
        Inventory inventory,
        MySqlConnection connection,
        IReadOnlyList<Item> items)
    {
        PersistenceGate.EnterOperation();
        try
        {
            using (AcquireFarmhandMutation(inventory))
            {
                using var transaction = connection.BeginTransaction();
                var snapshots = items.Select(manager.CapturePersistenceSnapshot).ToArray();
                Assert.Equal(snapshots.Length, manager.PersistSnapshots(connection, transaction, snapshots));
                transaction.Commit();
            }
        }
        finally
        {
            PersistenceGate.ExitOperation();
        }
    }

    private static InventoryMutationLease AcquireFarmhandMutation(Inventory inventory)
    {
        Assert.True(inventory.TryAcquireFarmhandMutation(out var mutation));
        Assert.NotNull(mutation);
        return mutation;
    }

    private static BaseMail CreateMail(uint receiverId) => new()
    {
        MailType = MailType.Butler,
        Title = "Returned farmhand items",
        ReceiverName = "Transaction Receiver",
        OpenDate = PersistedTime,
        Header =
        {
            SenderId = receiverId,
            SenderName = "Transaction Receiver",
            ReceiverId = receiverId,
            Status = MailStatus.Unread
        },
        Body =
        {
            Text = "Farmhand storage return",
            SendDate = PersistedTime,
            RecvDate = PersistedTime
        }
    };

    private static long ScalarOn(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static async Task<long> CountAsync(
        MySqlConnection connection,
        string table,
        string column,
        object value)
    {
        var allowed = (table, column) is
            ("character_butlers", "character_id") or
            ("mails", "id");
        if (!allowed)
            throw new ArgumentOutOfRangeException(nameof(table));
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM `{table}` WHERE `{column}`=@value";
        command.Parameters.AddWithValue("@value", value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<ItemRow> ReadItemAsync(MySqlConnection connection, ulong itemId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT container_id,slot_type,slot,count,owner FROM items WHERE id=@id";
        command.Parameters.AddWithValue("@id", itemId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return new ItemRow(
            reader.GetFieldValue<ulong>(0),
            (SlotType)reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            Convert.ToUInt64(reader.GetValue(4)));
    }

    private static void SetPrivateField(object target, string name, object value) =>
        target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(target, value);

    private static T GetPrivateField<T>(object target, string name) =>
        (T)target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(target)!;

    private sealed record InventoryState(
        Character Character,
        Inventory Inventory,
        ItemContainer System,
        ItemContainer Bag,
        Item[] Items);

    private sealed record ItemRow(
        ulong ContainerId,
        SlotType SlotType,
        int Slot,
        int Count,
        ulong OwnerId);
}
