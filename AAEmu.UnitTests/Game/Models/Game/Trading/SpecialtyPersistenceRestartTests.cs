using System.Data;
using System.Data.Common;
using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Trading;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AAEmu.UnitTests.Game.Models.Game.Trading;

/// <summary>
/// Executes the production item/purchase SQL against a file database, then reopens it without
/// an autosave. SQLite provides deterministic commit/rollback tests; MySQL lock concurrency
/// remains an integration concern (the adapter removes only SELECT's FOR UPDATE suffix).
/// </summary>
public class SpecialtyPersistenceRestartTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeathDrop_ReopenKeepsGroundPackAndIngredientsAtomic(bool failCommit)
    {
        using var database = new Database();
        using var world = new WorldInstance(new WorldTemplate { Id = 1, Name = "death-drop-restart" }, 0, true, 1);
        var player = new CharacterMock { Id = 1, ObjId = 1 };
        SetField(player, "_parentWorld", world);
        var inventory = DetachedInventory.Create(player);
        var partial = Place(new ItemMock(10, 4), inventory.Bag, 0);
        var exhausted = Place(new ItemMock(11), inventory.Bag, 1);
        var items = CreateItemManager(inventory);
        using (var connection = database.Open())
        using (var transaction = connection.BeginTransaction())
        {
            items.CaptureInventory(player.Id).Apply(connection, transaction);
            transaction.Commit();
        }

        partial.Count = 2;
        exhausted.Count = 0;
        inventory.Bag.Items.Remove(exhausted);
        items.ReleaseId(exhausted.Id);
        var pack = new Backpack(30, new BackpackTemplate
        {
            Id = 100, BackpackType = BackpackType.TradePack, UseSkillId = 35, MaxCount = 1
        }, 1);
        pack.InitializeFreshness(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), 8);
        Place(pack, inventory.Equipment, (int)EquipmentItemSlot.Backpack);
        var skills = Mock.Of<ISkillManager>();
        skills.GetSkillTemplate(35).Returns(new SkillTemplate
        {
            Id = 35, Effects = [new SkillEffect { Template = new PutDownBackpackEffect { BackpackDoodadId = 50 } }]
        });
        var doodad = new Doodad { ObjId = 40, TemplateId = 50 };
        SetField(doodad, "_parentWorld", world);
        var doodads = Mock.Of<IDoodadManager>();
        doodads.Create(world, 0, 50, player, true).Returns(doodad);
        var drop = new DatabaseDeathDrop(player, skills.Object, doodads.Object,
            Mock.Of<INonUnitObjectIdManager>().Object, Mock.Of<IMailManager>().Object, items, database, failCommit);

        await Assert.That(drop.TryDropOnDeath()).IsEqualTo(!failCommit);
        await Assert.That(drop.Spawned).IsEqualTo(!failCommit);
        await Assert.That(inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack) == pack).IsEqualTo(failCommit);

        using var restarted = database.Open();
        await Assert.That(Scalar(restarted, "SELECT count FROM items WHERE id = 10")).IsEqualTo(failCommit ? 4L : 2L);
        await Assert.That(Scalar(restarted, "SELECT COUNT(*) FROM items WHERE id = 11")).IsEqualTo(failCommit ? 1L : 0L);
        await Assert.That(Scalar(restarted, "SELECT COUNT(*) FROM items WHERE id = 30")).IsEqualTo(failCommit ? 0L : 1L);
        await Assert.That(Scalar(restarted, "SELECT COUNT(*) FROM doodads WHERE item_id = 30")).IsEqualTo(failCommit ? 0L : 1L);
        if (!failCommit)
        {
            await Assert.That(Scalar(restarted, "SELECT slot_type FROM items WHERE id = 30")).IsEqualTo((long)SlotType.System);
            using var command = restarted.CreateCommand();
            command.CommandText = "SELECT details FROM items WHERE id = 30";
            await Assert.That((byte[])command.ExecuteScalar()).IsEquivalentTo(pack.Detail);
        }
    }

    private sealed class DatabaseDeathDrop(Character owner, ISkillManager skills, IDoodadManager doodads,
        INonUnitObjectIdManager ids, IMailManager mail, IItemManager items, Database database, bool failCommit)
        : CharacterBackpackDrop(owner, skills, doodads, ids, mail)
    {
        public bool Spawned { get; private set; }
        protected override void Initialize(Doodad doodad) { }
        protected override bool Persist(Item item, Doodad doodad)
        {
            using var connection = database.Open();
            using var transaction = connection.BeginTransaction();
            DoodadItemPersistence.SavePlacement(connection, transaction, item, items.CaptureInventory(owner.Id),
                () => Execute(connection, transaction, "INSERT INTO doodads (id,item_id) VALUES (1,30)"));
            if (failCommit)
            {
                transaction.Rollback();
                return false;
            }
            transaction.Commit();
            return true;
        }
        protected override void Spawn(Doodad doodad) => Spawned = true;
    }

    [Test]
    [NotInParallel]
    public async Task BuySpecialty_WithdrawMovePurchasePublishesOnlyCommittedCargoAndSurvivesReopen()
    {
        using var database = new Database();
        var context = CreatePurchase(database, true);
        var player = context.Player;
        var cargo = context.Write.CargoItem;
        var items = Mock.Of<IItemManager>();
        items.CreateUnpersisted(cargo.TemplateId, 1, (byte)0).Returns(cargo);
        items.CaptureInventory(player.Id).Returns(context.Write.Inventory);
        var zones = Mock.Of<IZoneManager>();
        zones.GetZoneByKey(70).Returns(new Zone { ZoneKey = 70, GroupId = 8 });
        zones.GetZoneGroupById(8).Returns(new ZoneGroup { Id = 8, FactionChatRegionId = 2 });
        var store = new PurchaseStore(database);
        var manager = new SpecialtyManager(items.Object, Mock.Of<ILocalizationManager>().Object,
            Mock.Of<ISkillManager>().Object, zones.Object, Mock.Of<INpcManager>().Object,
            Mock.Of<IMailManager>().Object, null,
            Mock.Of<ISpecialtyMarketStore>().Object, store, Mock.Of<IWorldManager>().Object,
            Mock.Of<ITaskManager>().Object, TimeProvider.System, Options.Create(new AppConfiguration()));
        var tradeGood = new TradeGood
        {
            Id = 12,
            ItemId = cargo.TemplateId,
            Item = cargo.Template,
            TradeGoodCategoryId = 1,
            OutputCount = 5
        };
        cargo.Template.Refund = 200;
        SetField(manager, "_tradeGoodPurchaseSkill", new SkillTemplate { Id = 30, MaxRange = 5 });
        SetField(manager, "_tradeGoodsByCategoryAndItem", new Dictionary<(uint, uint), TradeGood> { [(1, cargo.TemplateId)] = tradeGood });
        SetField(manager, "_tradeGoodsByCategory", new Dictionary<uint, List<TradeGood>> { [1] = [tradeGood] });
        SetField(manager, "_tradeGoodMaterialsByTradeGoodId", new Dictionary<uint, List<TradeGoodMaterial>>
        {
            [tradeGood.Id] = [new TradeGoodMaterial { TradeGoodId = tradeGood.Id, TagId = 3361, RequiredCount = 50 }]
        });
        SetField(manager, "_tradeGoodCategories", new Dictionary<uint, TradeGoodCategory> { [1] = new() { Id = 1 } });
        SetField(manager, "_tradeGoodPriceIndices", new List<TradeGoodPriceIndex> { new() { Stock = -1, PriceIndex = 1000, Charge = 1000 } });
        SetField(manager, "_market", new SpecialtyMarketState { CargoStock = new() { [(8, 12)] = 2 } });
        using var world = new WorldInstance(new WorldTemplate { Id = 1, Name = "purchase-test" }, 0, true, 1);
        var npc = new Npc { ObjId = 40, Template = new NpcTemplate { TradeGoodBuy = true } };
        SetField(player, "_parentWorld", world);
        SetField(npc, "_parentWorld", world);
        SetField(npc.Transform, "_zoneId", 70u);
        world.AddObject(npc);
        player.CurrentInteractionObject = npc;
        var questField = typeof(Singleton<QuestManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousQuests = questField.GetValue(null);
        questField.SetValue(null, new QuestManager(Mock.Of<ITaskManager>().Object, zones.Object));
        var requirementsField = typeof(Singleton<UnitRequirementsGameData>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousRequirements = requirementsField.GetValue(null);
        var requirements = new UnitRequirementsGameData();
        SetField(requirements, "<_unitReqsByOwnerType>k__BackingField", new Dictionary<string, List<UnitReqs>>());
        requirementsField.SetValue(null, requirements);
        try
        {
            var bought = manager.BuySpecialty(player, npc.ObjId, manager.BuildBuyQuote(tradeGood, 2, 8));

            await Assert.That(bought).IsTrue();
            await Assert.That(store.Commits).IsEqualTo(1);
            await Assert.That(player.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack)).IsSameReferenceAs(cargo);
            await Assert.That(player.Inventory.Bag.GetItemBySlot(0)).IsSameReferenceAs(context.Write.PreviousBackpack);
            await Assert.That(player.Money).IsEqualTo(400L);
            await Assert.That(player.Money2).IsEqualTo(500L);
        }
        finally
        {
            questField.SetValue(null, previousQuests);
            requirementsField.SetValue(null, previousRequirements);
        }
        using var restarted = database.Open();
        await Assert.That(Scalar(restarted, "SELECT money FROM characters")).IsEqualTo(400L);
        await Assert.That(Scalar(restarted, "SELECT money2 FROM characters")).IsEqualTo(500L);
        await Assert.That(Scalar(restarted, "SELECT COUNT(*) FROM items WHERE id = 30")).IsEqualTo(1L);
        await Assert.That(Scalar(restarted, "SELECT stock FROM market")).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WithdrawMoveBuy_ReopenPreservesBankDebitAndReconciledSlots(bool moveToBank)
    {
        using var database = new Database();
        var context = CreatePurchase(database, moveToBank);
        var write = context.Write;
        using (var connection = database.Open())
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction, "UPDATE market SET stock = stock - 1");
            MySqlSpecialtyPurchaseStore.ApplyCharacterWrite(connection, transaction, write);
            transaction.Commit();
            await Assert.That(connection.LockingReads).IsEqualTo(2);
        }

        // Fresh connection sees only durable state, with no character/autosave publication.
        using var restarted = database.Open();
        await Assert.That(Scalar(restarted, "SELECT money FROM characters")).IsEqualTo(400L);
        await Assert.That(Scalar(restarted, "SELECT money2 FROM characters")).IsEqualTo(500L);
        await Assert.That(Scalar(restarted, "SELECT labor FROM accounts")).IsEqualTo(90L);
        await Assert.That(Scalar(restarted, "SELECT stock FROM market")).IsEqualTo(1L);
        await Assert.That(Scalar(restarted, "SELECT slot FROM items WHERE id = 20")).IsEqualTo(0L);
        await Assert.That(Scalar(restarted, "SELECT slot_type FROM items WHERE id = 20")).IsEqualTo((long)SlotType.Inventory);
        await Assert.That(Scalar(restarted, "SELECT slot_type FROM items WHERE id = 30")).IsEqualTo((long)SlotType.Equipment);
        await Assert.That(Scalar(restarted, "SELECT slot_type FROM items WHERE id = 10"))
            .IsEqualTo((long)(moveToBank ? SlotType.Bank : SlotType.Inventory));
        await Assert.That(context.MovedItem.IsDirty).IsTrue(); // A transaction never prematurely clears autosave dirtiness.
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PurchaseFailure_ReopenRollsBackWalletInventoryLaborAndStock(bool occupancyConflict)
    {
        using var database = new Database();
        var context = CreatePurchase(database, true);
        if (occupancyConflict)
        {
            // An unexpected row survives reconciliation; occupancy checks must still reject it.
            using var connection = database.Open();
            Execute(connection, null, $"INSERT INTO items (id,owner,container_id,slot_type,slot) VALUES " +
                $"(999,2,{context.Write.BagContainerId},{(int)SlotType.Inventory},0)");
        }
        using (var connection = database.Open())
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction, "UPDATE market SET stock = stock - 1");
            if (occupancyConflict)
                await Assert.That(() => MySqlSpecialtyPurchaseStore.ApplyCharacterWrite(connection, transaction, context.Write))
                    .Throws<SpecialtyPurchaseConflictException>();
            else
                MySqlSpecialtyPurchaseStore.ApplyCharacterWrite(connection, transaction, context.Write);
            // Models a failure after the item insert or a rejected claim, before commit.
            transaction.Rollback();
        }

        using var restarted = database.Open();
        await Assert.That(Scalar(restarted, "SELECT money FROM characters")).IsEqualTo(100L);
        await Assert.That(Scalar(restarted, "SELECT money2 FROM characters")).IsEqualTo(1000L);
        await Assert.That(Scalar(restarted, "SELECT labor FROM accounts")).IsEqualTo(100L);
        await Assert.That(Scalar(restarted, "SELECT stock FROM market")).IsEqualTo(2L);
        await Assert.That(Scalar(restarted, "SELECT COUNT(*) FROM items WHERE id = 30")).IsEqualTo(0L);
        await Assert.That(Scalar(restarted, "SELECT slot_type FROM items WHERE id = 10")).IsEqualTo((long)SlotType.Inventory);
        await Assert.That(Scalar(restarted, "SELECT slot_type FROM items WHERE id = 20")).IsEqualTo((long)SlotType.Equipment);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CraftedPackPlacement_ReopenKeepsProductAndIngredientConsumptionAtomic(bool failPlacement)
    {
        using var database = new Database();
        var player = new CharacterMock { Id = 1 };
        var inventory = DetachedInventory.Create(player);
        var partial = Place(new ItemMock(10, 4), inventory.Bag, 0);
        var exhausted = Place(new ItemMock(11), inventory.Bag, 1);
        var manager = CreateItemManager(inventory);
        using (var connection = database.Open())
        using (var transaction = connection.BeginTransaction())
        {
            manager.CaptureInventory(player.Id).Apply(connection, transaction);
            transaction.Commit();
        }

        // Completed craft before autosave: one stack is decremented, another fully consumed.
        partial.Count = 2;
        inventory.Bag.Items.Remove(exhausted);
        manager.ReleaseId(exhausted.Id);
        var pack = Place(new ItemMock(30, new BackpackTemplate { Id = 100, BackpackType = BackpackType.TradePack }),
            inventory.SystemContainer, 0);
        var snapshot = manager.CaptureInventory(player.Id);
        using (var connection = database.Open())
        using (var transaction = connection.BeginTransaction())
        {
            Action place = () => DoodadItemPersistence.SavePlacement(connection, transaction, pack, snapshot, () =>
            {
                Execute(connection, transaction, "INSERT INTO doodads (id,item_id) VALUES (1,30)");
                if (failPlacement)
                    throw new IOException("Placement failed before commit");
            });
            if (failPlacement)
            {
                await Assert.That(place).Throws<IOException>();
                transaction.Rollback();
            }
            else
            {
                place();
                transaction.Commit();
            }
        }

        using (var restarted = database.Open())
        {
            await Assert.That(Scalar(restarted, "SELECT count FROM items WHERE id = 10")).IsEqualTo(failPlacement ? 4L : 2L);
            await Assert.That(Scalar(restarted, "SELECT COUNT(*) FROM items WHERE id = 11")).IsEqualTo(failPlacement ? 1L : 0L);
            await Assert.That(Scalar(restarted, "SELECT COUNT(*) FROM items WHERE id = 30")).IsEqualTo(failPlacement ? 0L : 1L);
            await Assert.That(Scalar(restarted, "SELECT COUNT(*) FROM doodads WHERE item_id = 30")).IsEqualTo(failPlacement ? 0L : 1L);
        }
        if (failPlacement)
        {
            // Retry from the still-dirty live inventory after a failed transaction.
            using var connection = database.Open();
            using var transaction = connection.BeginTransaction();
            DoodadItemPersistence.SavePlacement(connection, transaction, pack, manager.CaptureInventory(player.Id),
                () => Execute(connection, transaction, "INSERT INTO doodads (id,item_id) VALUES (1,30)"));
            transaction.Commit();
            await Assert.That(Scalar(connection, "SELECT COUNT(*) FROM items WHERE id = 11")).IsEqualTo(0L);
        }
    }

    private static (SpecialtyPurchaseWrite Write, Item MovedItem, Character Player) CreatePurchase(Database database, bool moveToBank)
    {
        var player = new CharacterMock { Id = 1, AccountId = 1, Money = 100, Money2 = 1000 };
        var inventory = DetachedInventory.Create(player);
        var moved = Place(new ItemMock(10), inventory.Bag, 0);
        var glider = Place(new ItemMock(20, new BackpackTemplate { Id = 200, BackpackType = BackpackType.Glider }),
            inventory.Equipment, (int)EquipmentItemSlot.Backpack);
        var manager = CreateItemManager(inventory);
        using (var connection = database.Open())
        using (var transaction = connection.BeginTransaction())
        {
            manager.CaptureInventory(player.Id).Apply(connection, transaction);
            transaction.Commit();
        }
        player.ChangeMoney(SlotType.Bank, SlotType.Inventory, 500);
        inventory.Bag.Items.Remove(moved);
        Place(moved, moveToBank ? inventory.Warehouse : inventory.Bag, 2);
        var cargo = new ItemMock(30, new BackpackTemplate { Id = 300, BackpackType = BackpackType.TradeGoods })
        {
            OwnerId = player.Id, SlotType = SlotType.Equipment, Slot = (int)EquipmentItemSlot.Backpack,
            _holdingContainer = inventory.Equipment
        };
        return (new SpecialtyPurchaseWrite(1, 1, player.Money, player.Money - 200,
            100, 90, 0, 0, cargo, glider, inventory.Bag.ContainerId, 0,
            new SpecialtyMarketWrite(new(), new()), player.Money2, manager.CaptureInventory(player.Id)), moved, player);
    }

    private static void SetField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) continue;
            field.SetValue(target, value);
            return;
        }
        throw new InvalidOperationException($"Missing field {name}");
    }

    private sealed class PurchaseStore(Database database) : ISpecialtyPurchaseStore
    {
        public int Commits { get; private set; }
        public bool Commit(SpecialtyPurchaseWrite write)
        {
            using var connection = database.Open();
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, "UPDATE market SET stock = stock - 1");
            MySqlSpecialtyPurchaseStore.ApplyCharacterWrite(connection, transaction, write);
            transaction.Commit();
            Commits++;
            return true;
        }
    }

    private static Item Place(Item item, ItemContainer container, int slot)
    {
        item.OwnerId = container.OwnerId;
        item._holdingContainer = container;
        item.SlotType = container.ContainerType;
        item.Slot = slot;
        container.Items.Add(item);
        return item;
    }

    private static ItemManager CreateItemManager(Inventory inventory)
    {
        var manager = new ItemManager(Mock.Of<ISkillManager>().Object, Mock.Of<IItemIdManager>().Object,
            Mock.Of<IContainerIdManager>().Object, Mock.Of<ILocalizationManager>().Object,
            Mock.Of<ITaskManager>().Object, Mock.Of<IWorldManager>().Object);
        Set("_allPersistentContainers", inventory._itemContainers.Values.ToDictionary(x => x.ContainerId));
        Set("_allItems", inventory._itemContainers.Values.SelectMany(x => x.Items).ToDictionary(x => x.Id));
        Set("_removedItems", new List<ulong>());
        return manager;
        void Set(string name, object value) => typeof(ItemManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, value);
    }

    private static void Execute(DbConnection connection, DbTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private sealed class Database : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"aaemu-specialty-{Guid.NewGuid():N}.sqlite3");
        public Database()
        {
            using var connection = Open();
            Execute(connection, null, """
                CREATE TABLE characters (id INTEGER PRIMARY KEY, account_id INTEGER, money INTEGER, money2 INTEGER);
                INSERT INTO characters VALUES (1,1,100,1000);
                CREATE TABLE accounts (account_id INTEGER PRIMARY KEY, labor INTEGER, local_labor INTEGER);
                INSERT INTO accounts VALUES (1,100,0);
                CREATE TABLE market (stock INTEGER);
                INSERT INTO market VALUES (2);
                CREATE TABLE doodads (id INTEGER PRIMARY KEY, item_id INTEGER);
                CREATE TABLE item_containers (container_id INTEGER PRIMARY KEY, container_type TEXT, slot_type INTEGER,
                    container_size INTEGER, owner_id INTEGER, mate_id INTEGER, parent_item_id INTEGER);
                CREATE TABLE items (id INTEGER PRIMARY KEY, type TEXT, template_id INTEGER, container_id INTEGER,
                    slot_type INTEGER, slot INTEGER, count INTEGER, detail_type INTEGER, details BLOB, lifespan_mins INTEGER,
                    made_unit_id INTEGER, unsecure_time TEXT, unpack_time TEXT, owner INTEGER, created_at TEXT, grade INTEGER,
                    flags INTEGER, ucc INTEGER, expire_time TEXT, expire_online_minutes INTEGER, charge_time TEXT, charge_count INTEGER);
                """);
        }
        public LockingSqliteConnection Open()
        {
            var connection = new LockingSqliteConnection($"Data Source={_path};Pooling=False");
            connection.Open();
            return connection;
        }
        public void Dispose() => File.Delete(_path);
    }

    private sealed class LockingSqliteConnection(string connectionString) : DbConnection
    {
        private readonly SqliteConnection _inner = new(connectionString);
        public int LockingReads { get; private set; }
        public override string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
        public override string Database => _inner.Database;
        public override string DataSource => _inner.DataSource;
        public override string ServerVersion => _inner.ServerVersion;
        public override ConnectionState State => _inner.State;
        public override void Open() => _inner.Open();
        public override void Close() => _inner.Close();
        public override void ChangeDatabase(string name) => _inner.ChangeDatabase(name);
        protected override DbTransaction BeginDbTransaction(IsolationLevel level) => _inner.BeginTransaction(level);
        protected override DbCommand CreateDbCommand() => new Command(_inner.CreateCommand(), this);
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
        private sealed class Command(DbCommand inner, LockingSqliteConnection owner) : DbCommand
        {
            public override string CommandText
            {
                get => inner.CommandText;
                set
                {
                    if (value.EndsWith(" FOR UPDATE", StringComparison.Ordinal))
                    {
                        owner.LockingReads++;
                        value = value[..^11];
                    }
                    inner.CommandText = value;
                }
            }
            public override int CommandTimeout { get => inner.CommandTimeout; set => inner.CommandTimeout = value; }
            public override CommandType CommandType { get => inner.CommandType; set => inner.CommandType = value; }
            public override bool DesignTimeVisible { get => inner.DesignTimeVisible; set => inner.DesignTimeVisible = value; }
            public override UpdateRowSource UpdatedRowSource { get => inner.UpdatedRowSource; set => inner.UpdatedRowSource = value; }
            protected override DbConnection DbConnection { get => owner; set { } }
            protected override DbTransaction DbTransaction { get => inner.Transaction; set => inner.Transaction = value; }
            protected override DbParameterCollection DbParameterCollection => inner.Parameters;
            public override void Cancel() => inner.Cancel();
            public override int ExecuteNonQuery() => inner.ExecuteNonQuery();
            public override object ExecuteScalar() => inner.ExecuteScalar();
            public override void Prepare() => inner.Prepare();
            protected override DbParameter CreateDbParameter() => inner.CreateParameter();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => inner.ExecuteReader(behavior);
            protected override void Dispose(bool disposing)
            {
                if (disposing) inner.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
