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
            CREATE TABLE characters (
                id INTEGER PRIMARY KEY, money INTEGER NOT NULL, aa_point INTEGER NOT NULL,
                money2 INTEGER NOT NULL, bank_aa_point INTEGER NOT NULL, deleted INTEGER NOT NULL);
            CREATE TABLE ics_shop_items (shop_id INTEGER PRIMARY KEY, remaining INTEGER NOT NULL);
            CREATE TABLE audit_ics_sales (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                buyer_account INTEGER NOT NULL, buyer_char INTEGER NOT NULL,
                target_account INTEGER NOT NULL, target_char INTEGER NOT NULL,
                sale_date TEXT NOT NULL, shop_item_id INTEGER NOT NULL, sku INTEGER NOT NULL,
                sale_cost INTEGER NOT NULL, sale_currency INTEGER NOT NULL, description TEXT NOT NULL);
            INSERT INTO accounts VALUES (11, 100, 80);
            INSERT INTO characters VALUES (22, 900, 50, 400, 25, 0);
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
            var correlation = CashShopLogCorrelation.ForBuyer(AccountId, CharacterId);
            await Assert.That(messages).Contains(
                $"ICS purchase persistence failed for buyer correlation {correlation}");

            // The point of the correlation hash is that the raw ids are not in the line. That is asserted by
            // comparing the whole message against the ids standing alone as tokens, NOT by substring search:
            // AccountId is 11 and the correlation is a 16-character hex digest, so "11" can land inside it by
            // chance. Per start position that is (1/16)^2 for a two-character id, over 15 start positions, so a
            // run of this test fails for an unrelated reason roughly 5% of the time per id and about 10% with
            // both ids asserted. A substring check on a two-digit id against a random hex string mostly tests
            // the digest's alphabet, not the logging.
            AssertIdentifiersAbsent(messages, correlation);

            // And the two are genuinely different values, not the id relabelled.
            await Assert.That(correlation).IsNotEqualTo(AccountId.ToString());
            await Assert.That(correlation).IsNotEqualTo(CharacterId.ToString());
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

        var result = CashShopPurchaseStore.Stage(_connection, transaction, Commit(plan, liveAaPoints: 5), _ => true);
        transaction.Rollback();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Reason).IsEqualTo(CashShopPersistenceFailureReason.InsufficientAaPoints);
        await Assert.That(ReadRemaining(ShopId)).IsEqualTo(5);
        await Assert.That(CountAudit()).IsEqualTo(0);
    }

    [Test]
    public async Task Stage_WritesTheWholeLiveWallet_SoABankTransferSurvivesThePurchase()
    {
        // A withdrawal that only moved money in memory: the row still holds the pre-withdrawal bank gold while
        // the live wallet has already moved it to the pocket. The purchase must write both sides.
        SetAccount("money", CharacterId, 1_000);
        SetAccount("money2", CharacterId, 1_000);
        var plan = Plan(CashShopCurrencyType.Coins, cost: 200, quantity: 1);
        using var transaction = _connection.BeginTransaction();

        var result = CashShopPurchaseStore.Stage(_connection, transaction,
            Commit(plan, liveMoney: 1_800, liveAaPoints: 50, liveMoney2: 200, liveBankAaPoints: 25), _ => true);
        transaction.Commit();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(ReadAccount("money", CharacterId)).IsEqualTo(1_600L);
        await Assert.That(ReadAccount("money2", CharacterId)).IsEqualTo(200L);
        await Assert.That(ReadAccount("aa_point", CharacterId)).IsEqualTo(50L);
        await Assert.That(ReadAccount("bank_aa_point", CharacterId)).IsEqualTo(25L);
        await Assert.That(result.Money).IsEqualTo(1_600L);
        await Assert.That(result.Money2).IsEqualTo(200L);
        await Assert.That(result.AaPoints).IsEqualTo(50L);
        await Assert.That(result.BankAaPoints).IsEqualTo(25L);
    }

    [Test]
    public async Task Stage_MovesTheBankAaPointSideToo_WhenTheCartSpendsAaPoints()
    {
        SetAccount("bank_aa_point", CharacterId, 5_000);
        var plan = Plan(CashShopCurrencyType.AaPoints, cost: 12, quantity: 1);
        using var transaction = _connection.BeginTransaction();

        var result = CashShopPurchaseStore.Stage(_connection, transaction,
            Commit(plan, liveMoney: 900, liveAaPoints: 4_800, liveMoney2: 400, liveBankAaPoints: 4_500), _ => true);
        transaction.Commit();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(ReadAccount("aa_point", CharacterId)).IsEqualTo(4_788L);
        await Assert.That(ReadAccount("bank_aa_point", CharacterId)).IsEqualTo(4_500L);
        await Assert.That(result.BankAaPoints).IsEqualTo(4_500L);
    }

    [Test]
    public async Task Stage_LeavesTheCharacterRowAlone_WhenTheCartCostsNeitherGoldNorAaPoints()
    {
        // A credits-only cart has no wallet debit, so the live snapshot must not be written over a row another
        // path may have made newer than this task's read.
        SetAccount("money", CharacterId, 777);
        SetAccount("money2", CharacterId, 333);
        SetAccount("aa_point", CharacterId, 11);
        var plan = Plan(CashShopCurrencyType.Credits, cost: 20, quantity: 1);
        using var transaction = _connection.BeginTransaction();

        var result = CashShopPurchaseStore.Stage(_connection, transaction,
            Commit(plan, liveMoney: 4_242, liveAaPoints: 4_243, liveMoney2: 4_244, liveBankAaPoints: 4_245), _ => true);
        transaction.Commit();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(ReadAccount("money", CharacterId)).IsEqualTo(777L);
        await Assert.That(ReadAccount("money2", CharacterId)).IsEqualTo(333L);
        await Assert.That(ReadAccount("aa_point", CharacterId)).IsEqualTo(11L);
        await Assert.That(ReadAccount("bank_aa_point", CharacterId)).IsEqualTo(25L);
        await Assert.That(ReadAccount("credits", AccountId)).IsEqualTo(80L);
    }

    [Test]
    public async Task Stage_RefusesToWrite_A_NegativeLiveBankBalance()
    {
        var plan = Plan(CashShopCurrencyType.AaPoints, cost: 1, quantity: 1);
        using var transaction = _connection.BeginTransaction();

        var result = CashShopPurchaseStore.Stage(_connection, transaction,
            Commit(plan, liveMoney2: -1), _ => true);
        transaction.Rollback();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Reason).IsEqualTo(CashShopPersistenceFailureReason.PersistenceUnavailable);
        await Assert.That(ReadAccount("money", CharacterId)).IsEqualTo(900L);
        await Assert.That(ReadAccount("money2", CharacterId)).IsEqualTo(400L);
        await Assert.That(CountAudit()).IsEqualTo(0);
    }

    private CashShopPurchaseCommit Commit(CashShopPurchasePlan plan, long liveMoney = 900, long liveAaPoints = 50,
        long liveMoney2 = 400, long liveBankAaPoints = 25) => new(
        AccountId,
        CharacterId,
        TargetAccountId,
        TargetCharacterId,
        new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc),
        plan,
        [new BaseMail { ReceiverName = "target" }],
        liveMoney,
        liveAaPoints,
        liveMoney2,
        liveBankAaPoints);

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

    /// <summary>
    /// Fails when a raw account or character id appears in the captured log as a value of its own — a word
    /// bounded by non-digits, or a standalone number. The correlation token is removed first, because it is a
    /// hex digest that can contain a two-digit id by chance and says nothing about the ids being logged.
    /// </summary>
    private static void AssertIdentifiersAbsent(string messages, string correlation)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(correlation);

        var withoutCorrelation = messages.Replace(correlation, "<correlation>", StringComparison.Ordinal);

        foreach (var id in new[] { AccountId, CharacterId, TargetAccountId, TargetCharacterId })
        {
            var digits = id.ToString();
            var index = withoutCorrelation.IndexOf(digits, StringComparison.Ordinal);
            while (index >= 0)
            {
                var boundedOnLeft = index == 0 || !char.IsDigit(withoutCorrelation[index - 1]);
                var end = index + digits.Length;
                var boundedOnRight = end >= withoutCorrelation.Length || !char.IsDigit(withoutCorrelation[end]);
                if (boundedOnLeft && boundedOnRight)
                    Assert.Fail($"The captured log contains the raw id {digits}: {withoutCorrelation}");

                index = withoutCorrelation.IndexOf(digits, index + 1, StringComparison.Ordinal);
            }
        }
    }
}
