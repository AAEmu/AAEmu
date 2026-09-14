using AAEmu.Commons.Network.Core;
using System.Runtime.CompilerServices;
using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Expeditions.Activities;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using MySql.Data.MySqlClient;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AAEmu.IntegrationTests;

[Collection(ExpeditionCoreStaticCollection.Name)]
public sealed class ExpeditionActivityPersistenceIntegrationTests(ButlerMySqlFixture fixture)
    : IClassFixture<ButlerMySqlFixture>
{
    [Fact]
    public async Task ManagementAndShopHistories_FollowCallerTransactionCommitAndRollback()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_BUTLER_TEST_MYSQL to run the isolated MySQL fixture.");
        const uint expeditionId = 70_001;
        var usedAt = new DateTime(2026, 9, 13, 12, 30, 0, DateTimeKind.Utc);
        var repository = new MySqlExpeditionActivityRepository(fixture);
        await using var connection = await fixture.OpenAsync();

        using (var rollback = connection.BeginTransaction())
        {
            repository.AddManagementHistory(expeditionId,
                new ExpeditionManagementHistory("Member", 1, 77, usedAt, 900, 3), connection,
                rollback);
            repository.AddShopHistory(expeditionId,
                new ExpeditionShopHistory("Member", 800, 2, 45, usedAt), connection,
                rollback);
            rollback.Rollback();
        }
        Assert.Equal(0L, await Count(connection, "expedition_management_histories", expeditionId));
        Assert.Equal(0L, await Count(connection, "expedition_shop_histories", expeditionId));

        using (var commit = connection.BeginTransaction())
        {
            repository.AddManagementHistory(expeditionId,
                new ExpeditionManagementHistory("Member", 1, 77, usedAt, 900, 3), connection,
                commit);
            repository.AddShopHistory(expeditionId,
                new ExpeditionShopHistory("Member", 800, 2, 45, usedAt), connection,
                commit);
            commit.Commit();
        }

        await using var verify = connection.CreateCommand();
        verify.CommandText = "SELECT m.member_name,m.history_type,m.amount,m.detail_id,m.detail_value,s.item_id,s.stack,s.amount FROM expedition_management_histories m JOIN expedition_shop_histories s ON s.expedition_id=m.expedition_id WHERE m.expedition_id=@id";
        verify.Parameters.AddWithValue("@id", expeditionId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Member", reader.GetString(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(77UL, reader.GetFieldValue<ulong>(2));
        Assert.Equal(900U, reader.GetFieldValue<uint>(3));
        Assert.Equal(3, reader.GetInt32(4));
        Assert.Equal(800, reader.GetInt32(5));
        Assert.Equal(2, reader.GetInt32(6));
        Assert.Equal(45UL, reader.GetFieldValue<ulong>(7));
    }

    [Fact]
    public async Task TryAddPortal_ConcurrentFirstWritesRespectLevelCapacity()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_BUTLER_TEST_MYSQL to run the isolated MySQL fixture.");
        const uint expeditionId = 70_002;
        await using (var connection = await fixture.OpenAsync())
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = "INSERT INTO expeditions(id) VALUES(@id)";
            seed.Parameters.AddWithValue("@id", expeditionId);
            await seed.ExecuteNonQueryAsync();
        }
        var repository = new MySqlExpeditionActivityRepository(fixture);
        var first = Task.Run(() => repository.TryAddPortal(Portal(expeditionId, "First"), 1));
        var second = Task.Run(() => repository.TryAddPortal(Portal(expeditionId, "Second"), 1));

        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(results.Where(result => result));
        await using var verifyConnection = await fixture.OpenAsync();
        Assert.Equal(1L, await Count(verifyConnection, "expedition_portals", expeditionId));
    }

    [Fact]
    public async Task InstanceHistoryAndMembers_FollowCallerTransactionCommitAndRollback()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_BUTLER_TEST_MYSQL to run the isolated MySQL fixture.");
        const uint expeditionId = 70_003;
        var recordedAt = new DateTime(2026, 9, 13, 14, 30, 0, DateTimeKind.Utc);
        var repository = new MySqlExpeditionActivityRepository(fixture);
        await using var connection = await fixture.OpenAsync();

        var rolledBack = InstanceHistory(recordedAt);
        using (var rollback = connection.BeginTransaction())
        {
            repository.AddInstanceHistory(expeditionId, rolledBack, connection, rollback);
            Assert.NotEqual(0UL, rolledBack.HistoryId);
            rollback.Rollback();
        }
        Assert.Equal(0L, await Count(connection, "expedition_instance_histories", expeditionId));
        Assert.Equal(0L, await CountHistoryMembers(connection, rolledBack.HistoryId));

        var committed = InstanceHistory(recordedAt);
        using (var commit = connection.BeginTransaction())
        {
            repository.AddInstanceHistory(expeditionId, committed, connection, commit);
            commit.Commit();
        }

        var rows = repository.GetInstanceHistories(expeditionId, 20);
        var row = Assert.Single(rows);
        Assert.Equal(committed.HistoryId, row.HistoryId);
        Assert.Equal(41U, row.InstanceRankDetailId);
        Assert.Equal(69U, row.InstanceId);
        Assert.Equal(125U, row.Score);
        Assert.Equal(ExpeditionInstancePlayResult.Win, row.PlayResult);
        Assert.Equal(recordedAt, row.RecordedAt);
        Assert.Collection(row.Members,
            first =>
            {
                Assert.Equal(101UL, first.CharacterId);
                Assert.Equal(committed.HistoryId, first.HistoryId);
                Assert.Equal(ExpeditionInstanceMemberStatus.Finished, first.Status);
            },
            second =>
            {
                Assert.Equal(102UL, second.CharacterId);
                Assert.Equal(committed.HistoryId, second.HistoryId);
                Assert.Equal(ExpeditionInstanceMemberStatus.Started, second.Status);
            });
    }

    [Fact]
    public async Task ChangeSponsor_AllowsLegacyAllianceRootToSelectChildAndRejectsAnotherAlliance()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_BUTLER_TEST_MYSQL to run the isolated MySQL fixture.");
        const uint expeditionId = 70_004;
        await using (var connection = await fixture.OpenAsync())
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = "INSERT INTO expeditions(id,mother) VALUES(@id,148)";
            seed.Parameters.AddWithValue("@id", expeditionId);
            await seed.ExecuteNonQueryAsync();
        }

        var actor = new Character(new UnitCustomModelParams()) { Id = 103 };
        actor.Connection = new GameConnection(new Mock<ISession>().Object) { ActiveChar = actor };
        var expedition = new Expedition
        {
            Id = (FactionsEnum)expeditionId,
            MotherId = (FactionsEnum)148,
            OwnerId = actor.Id,
            Members = [new ExpeditionMember { CharacterId = actor.Id, Role = byte.MaxValue }]
        };
        actor.Expedition = expedition;
        var world = new Mock<IWorldManager>();
        world.Setup(manager => manager.GetCharacterById(actor.Id)).Returns(actor);
        var factions = new Mock<IFactionManager>();
        factions.Setup(manager => manager.GetFaction((FactionsEnum)148)).Returns(Sponsor(148, 0));
        factions.Setup(manager => manager.GetFaction((FactionsEnum)101)).Returns(Sponsor(101, 148));
        factions.Setup(manager => manager.GetFaction((FactionsEnum)102)).Returns(Sponsor(102, 148));
        factions.Setup(manager => manager.GetFaction((FactionsEnum)108)).Returns(Sponsor(108, 149));
        var expeditionManager = new ExpeditionManager(
            new Mock<IExpeditionIdManager>().Object,
            new Mock<ITeamManager>().Object,
            world.Object,
            new Mock<IChatManager>().Object,
            new Mock<IExpeditionPersistenceConnectionFactory>().Object,
            new Mock<IItemManager>().Object,
            factions.Object);
        var service = new ExpeditionActivityService(new MySqlExpeditionActivityRepository(fixture), world.Object,
            expeditionManager, new Mock<IItemManager>().Object, fixture, factions.Object);

        Assert.True(service.ChangeSponsor(actor, 148, 101));
        Assert.Equal((FactionsEnum)101, expedition.MotherId);
        await using (var connection = await fixture.OpenAsync())
            Assert.Equal(101L, await Scalar(connection, "SELECT mother FROM expeditions WHERE id=@id", expeditionId));

        Assert.False(service.ChangeSponsor(actor, 101, 108));
        Assert.Equal((FactionsEnum)101, expedition.MotherId);
        await using (var connection = await fixture.OpenAsync())
            Assert.Equal(101L, await Scalar(connection, "SELECT mother FROM expeditions WHERE id=@id", expeditionId));
    }

    [Fact]
    public async Task ContributionShop_CommitsDebitItemLimitAndHistoryAndRollsBackFailedItemCreation()
    {
        Assert.SkipUnless(fixture.Enabled, "Set AAEMU_BUTLER_TEST_MYSQL to run the isolated MySQL fixture.");
        const uint expeditionId = 70_005;
        const uint characterId = 70_105;
        const uint firstItemId = 80_001;
        const uint postCommitItemId = 80_002;
        const uint rollbackItemId = 80_003;
        const uint firstObjectId = 90_001;
        const uint postCommitObjectId = 90_002;
        const uint rollbackObjectId = 90_003;
        await using (var connection = await fixture.OpenAsync())
        await using (var seed = connection.CreateCommand())
        {
            seed.CommandText = "INSERT INTO expeditions(id) VALUES(@expedition_id); " +
                               "INSERT INTO expedition_members(character_id,expedition_id,contribution_point) " +
                               "VALUES(@character_id,@expedition_id,200)";
            seed.Parameters.AddWithValue("@expedition_id", expeditionId);
            seed.Parameters.AddWithValue("@character_id", characterId);
            await seed.ExecuteNonQueryAsync();
        }

        var itemIds = new Mock<IItemIdManager>();
        itemIds.SetupSequence(manager => manager.GetNextId()).Returns(firstObjectId).Returns(postCommitObjectId)
            .Returns(rollbackObjectId);
        var itemManager = CreateItemManager(itemIds, firstItemId, postCommitItemId, rollbackItemId);
        SingletonContainer.ServiceProvider = new ServiceCollection().AddSingleton(itemManager).BuildServiceProvider();
        try
        {
            var character = CreateCharacter(characterId);
            character.Name = "Shop Member";
            CreateInventory(character, 71_005);
            var member = new ExpeditionMember
            {
                CharacterId = characterId,
                ExpeditionId = (FactionsEnum)expeditionId,
                Name = character.Name,
                ContributionPoint = 200
            };
            var expedition = new Expedition
            {
                Id = (FactionsEnum)expeditionId,
                Members = [member]
            };
            character.Expedition = expedition;
            var world = new Mock<IWorldManager>();
            world.Setup(manager => manager.GetCharacterById(characterId)).Returns(character);
            world.Setup(manager => manager.GetAllCharacters()).Returns([character]);
            var expeditionManager = new ExpeditionManager(
                new Mock<IExpeditionIdManager>().Object,
                new Mock<ITeamManager>().Object,
                world.Object,
                new Mock<IChatManager>().Object);
            var npcManager = new NpcManager(
                new Mock<IObjectIdManager>().Object,
                new Mock<IModelManager>().Object,
                new Mock<IFactionManager>().Object,
                itemManager,
                new Mock<ITaskManager>().Object);
            var repository = new MySqlExpeditionActivityRepository(fixture);
            var service = new ExpeditionActivityService(repository, world.Object, expeditionManager, itemManager,
                fixture, new Mock<IFactionManager>().Object, TimeProvider.System, npcManager);
            var pack = new MerchantGoods(304, MerchantPackKind.ItemPoint, 0);
            var firstGood = Good(firstItemId, 30, MerchantPurchaseType.Weekly, 5);
            var alternateRequestedGrade = Good(firstItemId, 30, MerchantPurchaseType.Weekly, 5, grade: 1);
            var postCommitGood = Good(postCommitItemId, 10, MerchantPurchaseType.Always, 5);
            var rollbackGood = Good(rollbackItemId, 10, MerchantPurchaseType.Always, 5);
            pack.AddItemToStock(firstGood);
            pack.AddItemToStock(alternateRequestedGrade);
            pack.AddItemToStock(postCommitGood);
            pack.AddItemToStock(rollbackGood);

            Assert.True(service.TryPurchaseContributionGoods(character, pack,
                [(firstGood, 1), (alternateRequestedGrade, 1)],
                out _, out var committedLimits));
            Assert.Equal(140U, member.ContributionPoint);
            Assert.Equal(2, committedLimits[firstItemId].BuyCount);
            var committedItem = Assert.Single(character.Inventory.Bag.Items);
            Assert.Equal(firstObjectId, committedItem.Id);
            Assert.Equal(2, committedItem.Count);
            Assert.Equal(firstItemId, committedItem.TemplateId);

            Assert.True(service.TryPurchaseContributionGoods(character, pack, [(firstGood, 3)],
                out _, out committedLimits));
            Assert.Equal(50U, member.ContributionPoint);
            Assert.Equal(5, committedLimits[firstItemId].BuyCount);
            Assert.Same(committedItem, Assert.Single(character.Inventory.Bag.Items));
            Assert.Equal(5, committedItem.Count);
            await using (var verify = await fixture.OpenAsync())
            {
                Assert.Equal(50L, await Scalar(verify,
                    "SELECT contribution_point FROM expedition_members WHERE character_id=@id", characterId));
                Assert.Equal(5L, await Scalar(verify,
                    "SELECT count FROM items WHERE id=@id", firstObjectId));
                Assert.Equal(5L, await Scalar(verify,
                    "SELECT buy_count FROM character_merchant_purchases WHERE character_id=@id AND item_id=80001",
                    characterId));
                Assert.Equal(3L, await Scalar(verify,
                    "SELECT COUNT(*) FROM expedition_shop_histories WHERE expedition_id=@id AND item_id=80001 " +
                    "AND ((stack=1 AND amount=30) OR (stack=3 AND amount=90))", expeditionId));
            }

            var throwingSession = new Mock<ISession>();
            throwingSession.Setup(session => session.SendPacket(It.IsAny<byte[]>()))
                .Throws(new InvalidOperationException("forced post-commit publication failure"));
            character.Connection = new GameConnection(throwingSession.Object) { ActiveChar = character };
            Assert.True(service.TryPurchaseContributionGoods(character, pack, [(postCommitGood, 1)],
                out _, out _));
            Assert.Equal(40U, member.ContributionPoint);
            var postCommitItem = Assert.Single(character.Inventory.Bag.Items,
                item => item.TemplateId == postCommitItemId);
            Assert.Equal(postCommitObjectId, postCommitItem.Id);
            Assert.Same(postCommitItem, itemManager.GetItemByItemId(postCommitObjectId));
            itemIds.Verify(manager => manager.ReleaseId(postCommitObjectId), Times.Never);
            await using (var verify = await fixture.OpenAsync())
            {
                Assert.Equal(40L, await Scalar(verify,
                    "SELECT contribution_point FROM expedition_members WHERE character_id=@id", characterId));
                Assert.Equal(1L, await Scalar(verify,
                    "SELECT count FROM items WHERE id=@id", postCommitObjectId));
                Assert.Equal(1L, await Scalar(verify,
                    "SELECT buy_count FROM character_merchant_purchases WHERE character_id=@id AND item_id=80002",
                    characterId));
                Assert.Equal(1L, await Scalar(verify,
                    "SELECT COUNT(*) FROM expedition_shop_histories WHERE expedition_id=@id AND item_id=80002 " +
                    "AND stack=1 AND amount=10", expeditionId));
            }

            await using (var triggerConnection = await fixture.OpenAsync())
            await using (var trigger = triggerConnection.CreateCommand())
            {
                trigger.CommandText = "CREATE TRIGGER fail_expedition_shop_history BEFORE INSERT ON " +
                                      "expedition_shop_histories FOR EACH ROW SIGNAL SQLSTATE '45000' " +
                                      "SET MESSAGE_TEXT='forced shop rollback'";
                await trigger.ExecuteNonQueryAsync();
            }
            try
            {
                Assert.False(service.TryPurchaseContributionGoods(character, pack, [(rollbackGood, 1)],
                    out _, out _));
            }
            finally
            {
                await using var cleanupConnection = await fixture.OpenAsync();
                await using var cleanup = cleanupConnection.CreateCommand();
                cleanup.CommandText = "DROP TRIGGER IF EXISTS fail_expedition_shop_history";
                await cleanup.ExecuteNonQueryAsync();
            }

            Assert.Equal(40U, member.ContributionPoint);
            Assert.DoesNotContain(character.Inventory.Bag.Items, item => item.TemplateId == rollbackItemId);
            Assert.Null(itemManager.GetItemByItemId(rollbackObjectId));
            itemIds.Verify(manager => manager.ReleaseId(rollbackObjectId), Times.Once);
            await using (var verify = await fixture.OpenAsync())
            {
                Assert.Equal(40L, await Scalar(verify,
                    "SELECT contribution_point FROM expedition_members WHERE character_id=@id", characterId));
                Assert.Equal(0L, await Scalar(verify, "SELECT COUNT(*) FROM items WHERE id=@id", rollbackObjectId));
                Assert.Equal(0L, await Scalar(verify,
                    "SELECT COUNT(*) FROM character_merchant_purchases WHERE character_id=@id AND item_id=80003",
                    characterId));
                Assert.Equal(0L, await Scalar(verify,
                    "SELECT COUNT(*) FROM expedition_shop_histories WHERE expedition_id=@id AND item_id=80003",
                    expeditionId));
            }
        }
        finally
        {
            SingletonContainer.ServiceProvider = null;
            typeof(Singleton<ItemManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
        }
    }

    private static ExpeditionPortalPoint Portal(uint expeditionId, string name) => new()
    {
        ExpeditionId = expeditionId,
        Name = name,
        ZoneId = 10,
        X = 1,
        Y = 2,
        Z = 3,
        ZRot = 90
    };

    private static ExpeditionInstanceHistory InstanceHistory(DateTime recordedAt) => new()
    {
        InstanceRankDetailId = 41,
        InstanceId = 69,
        Score = 125,
        PlayResult = ExpeditionInstancePlayResult.Win,
        RecordedAt = recordedAt,
        Members =
        [
            new ExpeditionInstanceHistoryMember(0, 101, ExpeditionInstanceMemberStatus.Finished),
            new ExpeditionInstanceHistoryMember(0, 102, ExpeditionInstanceMemberStatus.Started)
        ]
    };

    private static SystemFaction Sponsor(uint id, uint motherId) => new()
    {
        Id = (FactionsEnum)id,
        MotherId = (FactionsEnum)motherId,
        ShowCreateExpedition = true,
        Name = $"Sponsor {id}",
        OwnerName = string.Empty
    };

    private static MerchantGoodsItem Good(uint itemId, int cost, MerchantPurchaseType purchaseType, int limit,
        byte grade = 0) =>
        new()
        {
            Id = itemId,
            ItemTemplateId = itemId,
            Grade = grade,
            Cost = cost,
            Currency = ShopCurrencyType.ItemPoint,
            PurchaseType = purchaseType,
            PurchaseLimit = limit
        };

    private static Character CreateCharacter(uint id)
    {
        var character = new Character(new UnitCustomModelParams()) { Id = id };
        character.Connection = new GameConnection(new Mock<ISession>().Object) { ActiveChar = character };
        return character;
    }

    private static ItemManager CreateItemManager(Mock<IItemIdManager> itemIds, params uint[] templateIds)
    {
        var manager = new ItemManager(
            new Mock<ISkillManager>().Object,
            itemIds.Object,
            new Mock<IContainerIdManager>().Object,
            new Mock<ILocalizationManager>().Object,
            new Mock<ITaskManager>().Object,
            new Mock<IWorldManager>().Object);
        SetField(manager, "_allItems", new Dictionary<ulong, Item>());
        SetField(manager, "_removedItems", new List<ulong>());
        SetField(manager, "_allPersistentContainers", new Dictionary<ulong, ItemContainer>());
        SetField(manager, "_itemBagContainers", new Dictionary<ulong, ItemBagContainer>());
        SetField(manager, "_templates", templateIds.ToDictionary(id => id,
            id => new ItemTemplate { Id = id, MaxCount = 100, FixedGrade = 0 }));
        return manager;
    }

    private static void CreateInventory(Character character, ulong containerId)
    {
        var inventory = (Inventory)RuntimeHelpers.GetUninitializedObject(typeof(Inventory));
        typeof(Inventory).GetField(nameof(Inventory.Owner))!.SetValue(inventory, character);
        var bag = new ItemContainer(character.Id, SlotType.Inventory, false, character)
        {
            Owner = character,
            ContainerId = containerId,
            ContainerSize = 50
        };
        typeof(Inventory).GetProperty(nameof(Inventory.Bag))!.SetValue(inventory, bag);
        typeof(Inventory).GetProperty(nameof(Inventory._itemContainers))!.SetValue(inventory,
            new Dictionary<SlotType, ItemContainer> { [SlotType.Inventory] = bag });
        character.Inventory = inventory;
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static async Task<long> Count(MySqlConnection connection, string table, uint expeditionId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM `{table}` WHERE expedition_id=@id";
        command.Parameters.AddWithValue("@id", expeditionId);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> CountHistoryMembers(MySqlConnection connection, ulong historyId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM expedition_instance_history_members WHERE history_id=@history_id";
        command.Parameters.AddWithValue("@history_id", historyId);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> Scalar(MySqlConnection connection, string sql, uint expeditionId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", expeditionId);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
