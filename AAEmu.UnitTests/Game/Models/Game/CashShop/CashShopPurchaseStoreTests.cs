using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.StaticValues;
using Microsoft.Data.Sqlite;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace AAEmu.UnitTests.Game.Models.Game.CashShop;

[NotInParallel]
public sealed class CashShopPurchaseStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private const uint AccountId = 11;
    private const uint CharacterId = 22;
    private const uint TargetAccountId = 33;
    private const uint TargetCharacterId = 44;
    private const uint ShopId = 55;
    private const uint SkuId = 66;
    private const uint SecondShopId = 77;
    private const uint SecondSkuId = 88;

    public CashShopPurchaseStoreTests()
    {
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE accounts (account_id INTEGER PRIMARY KEY, credits INTEGER NOT NULL, loyalty INTEGER NOT NULL);
            CREATE TABLE characters (id INTEGER PRIMARY KEY, money INTEGER NOT NULL, aa_point INTEGER NOT NULL, deleted INTEGER NOT NULL);
            CREATE TABLE ics_shop_items (shop_id INTEGER PRIMARY KEY, remaining INTEGER NOT NULL);
            CREATE TABLE audit_ics_sales (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                buyer_account INTEGER NOT NULL, buyer_char INTEGER NOT NULL,
                target_account INTEGER NOT NULL, target_char INTEGER NOT NULL,
                sale_date TEXT NOT NULL, shop_item_id INTEGER NOT NULL, sku INTEGER NOT NULL,
                sale_cost INTEGER NOT NULL, sale_currency INTEGER NOT NULL, description TEXT NOT NULL);
            INSERT INTO accounts VALUES (11, 100, 80);
            INSERT INTO characters VALUES (22, 900, 50, 0);
            INSERT INTO ics_shop_items VALUES (55, 5);
            """;
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task Stage_CommitsBalancesStockAndAuditTogether()
    {
        var plan = Plan(CashShopCurrencyType.AaPoints, cost: 12, quantity: 2);
        using var transaction = _connection.BeginTransaction();
        var stagedMail = 0;

        var result = CashShopPurchaseStore.Stage(_connection, transaction, Commit(plan),
            _ => { stagedMail++; return true; });
        transaction.Commit();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(stagedMail).IsEqualTo(1);
        await Assert.That(result.AaPoints).IsEqualTo(38L);
        await Assert.That(ReadAccount("aa_point", CharacterId)).IsEqualTo(38L);
        await Assert.That(ReadAccount("credits", AccountId)).IsEqualTo(100L);
        await Assert.That(ReadRemaining(ShopId)).IsEqualTo(3);
        await Assert.That(CountAudit()).IsEqualTo(1);
    }

    [Test]
    public async Task DeliveryFailure_RollsBackTheAlreadyStagedDebitAndStock()
    {
        var plan = Plan(CashShopCurrencyType.AaPoints, cost: 12, quantity: 2);
        using var transaction = _connection.BeginTransaction();

        var result = CashShopPurchaseStore.Stage(_connection, transaction, Commit(plan), _ => false);
        transaction.Rollback();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Reason).IsEqualTo(CashShopPersistenceFailureReason.DeliveryFailed);
        await Assert.That(ReadAccount("aa_point", CharacterId)).IsEqualTo(50L);
        await Assert.That(ReadRemaining(ShopId)).IsEqualTo(5);
        await Assert.That(CountAudit()).IsEqualTo(0);
    }

    [Test]
    public async Task StockRaceFailure_LeavesEveryOtherWriteForRollback()
    {
        var plan = Plan(CashShopCurrencyType.AaPoints, cost: 12, quantity: 6);
        using var transaction = _connection.BeginTransaction();

        var result = CashShopPurchaseStore.Stage(_connection, transaction, Commit(plan), _ => true);
        transaction.Rollback();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Reason).IsEqualTo(CashShopPersistenceFailureReason.SoldOut);
        await Assert.That(ReadAccount("aa_point", CharacterId)).IsEqualTo(50L);
        await Assert.That(ReadRemaining(ShopId)).IsEqualTo(5);
        await Assert.That(CountAudit()).IsEqualTo(0);
    }

    [Test]
    public async Task MultiShopStockRace_ReportsTheShopThatActuallyLostStock()
    {
        InsertRemaining(SecondShopId, 1);
        var plan = new CashShopPurchasePlan(
            [
                new CashShopPurchaseLine(SkuId, ShopId, 0, 77, 1, 0, 0, CashShopCurrencyType.AaPoints, 6, "First"),
                new CashShopPurchaseLine(SecondSkuId, SecondShopId, 0, 78, 2, 0, 0, CashShopCurrencyType.AaPoints, 6, "Second")
            ],
            new Dictionary<CashShopCurrencyType, long> { [CashShopCurrencyType.AaPoints] = 12 },
            new Dictionary<uint, long> { [ShopId] = 1, [SecondShopId] = 2 });
        using var transaction = _connection.BeginTransaction();

        var result = CashShopPurchaseStore.Stage(_connection, transaction, Commit(plan), _ => true);
        transaction.Rollback();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Reason).IsEqualTo(CashShopPersistenceFailureReason.SoldOut);
        await Assert.That(result.FailedShopId).IsEqualTo(SecondShopId);
        await Assert.That(result.ClientFailure.ShopId).IsEqualTo(SecondShopId);
        await Assert.That(ReadRemaining(ShopId)).IsEqualTo(5);
        await Assert.That(ReadRemaining(SecondShopId)).IsEqualTo(1);
    }

    [Test]
    public async Task DatabaseException_IsLoggedReportedAndRolledBack()
    {
        using var target = new MemoryTarget("cash-shop-store-test") { Layout = "${message}" };
        var logging = new LoggingConfiguration();
        logging.AddTarget(target);
        logging.AddRule(LogLevel.Error, LogLevel.Fatal, target);
        var previousLogging = LogManager.Configuration;
        LogManager.Configuration = logging;
        try
        {
            DropAuditTable();
            var plan = Plan(CashShopCurrencyType.AaPoints, cost: 12, quantity: 2);
            using var transaction = _connection.BeginTransaction();

            var result = CashShopPurchaseStore.Stage(_connection, transaction, Commit(plan), _ => true);
            transaction.Rollback();

            var messages = string.Join(Environment.NewLine, target.Logs);
            await Assert.That(messages).Contains(
                $"ICS purchase persistence failed for account {AccountId} character {CharacterId}");
            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Reason).IsEqualTo(CashShopPersistenceFailureReason.PersistenceUnavailable);
            await Assert.That(result.ClientFailure.WireError).IsEqualTo(ErrorMessageType.IngameShopBuyFail);
            await Assert.That(ReadAccount("aa_point", CharacterId)).IsEqualTo(50L);
            await Assert.That(ReadRemaining(ShopId)).IsEqualTo(5);
        }
        finally
        {
            LogManager.Configuration = previousLogging;
        }
    }

    [Test]
    public async Task AaPointShortfall_IsDetectedByTheConditionalDebit()
    {
        SetAccount("aa_point", CharacterId, 5);
        var plan = Plan(CashShopCurrencyType.AaPoints, cost: 12, quantity: 1);
        using var transaction = _connection.BeginTransaction();

        var result = CashShopPurchaseStore.Stage(_connection, transaction, Commit(plan), _ => true);
        transaction.Rollback();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Reason).IsEqualTo(CashShopPersistenceFailureReason.InsufficientAaPoints);
        await Assert.That(ReadRemaining(ShopId)).IsEqualTo(5);
        await Assert.That(CountAudit()).IsEqualTo(0);
    }

    private CashShopPurchaseCommit Commit(CashShopPurchasePlan plan) => new(
        AccountId,
        CharacterId,
        TargetAccountId,
        TargetCharacterId,
        new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc),
        plan,
        [new BaseMail { ReceiverName = "target" }]);

    private static CashShopPurchasePlan Plan(CashShopCurrencyType currency, long cost, long quantity) =>
        new(
            [new CashShopPurchaseLine(SkuId, ShopId, 0, 77, (uint)quantity, 0, 0, currency, cost, "Content")],
            new Dictionary<CashShopCurrencyType, long> { [currency] = cost },
            new Dictionary<uint, long> { [ShopId] = quantity });

    private long ReadAccount(string column, uint id)
    {
        var table = column is "credits" or "loyalty" ? "accounts" : "characters";
        var key = table == "accounts" ? "account_id" : "id";
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM {table} WHERE {key}=@id";
        command.Parameters.AddWithValue("@id", id);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private int ReadRemaining(uint shopId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT remaining FROM ics_shop_items WHERE shop_id=@id";
        command.Parameters.AddWithValue("@id", shopId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private int CountAudit()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM audit_ics_sales";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private void InsertRemaining(uint shopId, int remaining)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "INSERT INTO ics_shop_items (shop_id,remaining) VALUES (@shop_id,@remaining)";
        command.Parameters.AddWithValue("@shop_id", shopId);
        command.Parameters.AddWithValue("@remaining", remaining);
        command.ExecuteNonQuery();
    }

    private void DropAuditTable()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DROP TABLE audit_ics_sales";
        command.ExecuteNonQuery();
    }

    private void SetAccount(string column, uint id, long value)
    {
        var table = column is "credits" or "loyalty" ? "accounts" : "characters";
        var key = table == "accounts" ? "account_id" : "id";
        using var command = _connection.CreateCommand();
        command.CommandText = $"UPDATE {table} SET {column}=@value WHERE {key}=@id";
        command.Parameters.AddWithValue("@value", value);
        command.Parameters.AddWithValue("@id", id);
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
