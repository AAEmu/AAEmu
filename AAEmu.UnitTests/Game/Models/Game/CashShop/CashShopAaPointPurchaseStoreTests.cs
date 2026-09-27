using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.CashShop;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.CashShop;

/// <summary>
/// Covers the AA-point checkout: a request must be quotable, and the charge, the grant, and the
/// journal row must land together or not at all.
/// </summary>
[NotInParallel]
public sealed class CashShopAaPointPurchaseStoreTests : IDisposable
{
    private const uint AccountId = 11;
    private const uint CharacterId = 22;
    private const long StartMoney = 900;
    private const long StartAaPoints = 50;
    private static readonly DateTime PurchaseDate =
        new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public CashShopAaPointPurchaseStoreTests()
    {
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE characters (
                id INTEGER PRIMARY KEY, money INTEGER NOT NULL, aa_point INTEGER NOT NULL,
                money2 INTEGER NOT NULL, bank_aa_point INTEGER NOT NULL, deleted INTEGER NOT NULL);
            CREATE TABLE audit_ics_aa_point_purchases (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_id INTEGER NOT NULL, character_id INTEGER NOT NULL,
                purchase_date TEXT NOT NULL, cash_spent INTEGER NOT NULL,
                aa_points INTEGER NOT NULL, exchange_ratio INTEGER NOT NULL);
            INSERT INTO characters VALUES (22, 900, 50, 400, 25, 0);
            """;
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task Stage_ChargesCashGrantsPointsAndJournalsThemTogether()
    {
        using var transaction = _connection.BeginTransaction();

        var result = CashShopAaPointPurchaseStore.Stage(_connection, transaction,
            Commit(cash: 3, points: 300, ratio: 100));
        transaction.Commit();

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(ReadWallet("money")).IsEqualTo(StartMoney - 3);
        await Assert.That(ReadWallet("aa_point")).IsEqualTo(StartAaPoints + 300);
        await Assert.That(result.Money).IsEqualTo(StartMoney - 3);
        await Assert.That(result.AaPoints).IsEqualTo(StartAaPoints + 300);
        await Assert.That(CountAudit()).IsEqualTo(1);
        await Assert.That(ReadAuditInt("cash_spent")).IsEqualTo(3L);
        await Assert.That(ReadAuditInt("aa_points")).IsEqualTo(300L);
        await Assert.That(ReadAuditInt("exchange_ratio")).IsEqualTo(100L);
    }

    [Test]
    public async Task Stage_LeavesTheBankSideOfTheWalletUntouched()
    {
        using var transaction = _connection.BeginTransaction();

        var result = CashShopAaPointPurchaseStore.Stage(_connection, transaction,
            Commit(cash: 4, points: 400, ratio: 100));
        transaction.Commit();

        await Assert.That(result.Succeeded).IsTrue();
        // The checkout moves pocket cash only; the bank is a different pocket and must not move.
        await Assert.That(ReadWallet("money2")).IsEqualTo(400L);
        await Assert.That(ReadWallet("bank_aa_point")).IsEqualTo(25L);
    }

    [Test]
    public async Task InsufficientCash_ChargesNothingAndJournalsNothing()
    {
        using var transaction = _connection.BeginTransaction();

        var result = CashShopAaPointPurchaseStore.Stage(_connection, transaction,
            Commit(cash: StartMoney + 1, points: 1_000, ratio: 100));
        transaction.Rollback();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Reason).IsEqualTo(CashShopAaPointFailureReason.InsufficientCash);
        await Assert.That(ReadWallet("money")).IsEqualTo(StartMoney);
        await Assert.That(ReadWallet("aa_point")).IsEqualTo(StartAaPoints);
        await Assert.That(CountAudit()).IsEqualTo(0);
    }


    [Test]
    public async Task MissingJournalTable_RollsBackTheChargeAndTheGrant()
    {
        DropAuditTable();
        using var transaction = _connection.BeginTransaction();

        var result = CashShopAaPointPurchaseStore.Stage(_connection, transaction,
            Commit(cash: 2, points: 200, ratio: 100));
        transaction.Rollback();

        // The grant and the charge are staged before the journal row, so a ledger that cannot be
        // written has to take both of them back down with it.
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReadWallet("money")).IsEqualTo(StartMoney);
        await Assert.That(ReadWallet("aa_point")).IsEqualTo(StartAaPoints);
    }

    [Test]
    public async Task DeletedCharacter_IsNotCharged()
    {
        SetDeleted();
        using var transaction = _connection.BeginTransaction();

        var result = CashShopAaPointPurchaseStore.Stage(_connection, transaction,
            Commit(cash: 1, points: 100, ratio: 100));
        transaction.Rollback();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(ReadWallet("aa_point")).IsEqualTo(StartAaPoints);
        await Assert.That(CountAudit()).IsEqualTo(0);
    }

    [Test]
    public async Task ASecondCheckoutChargesAgainRatherThanReusingTheFirstLedgerRow()
    {
        using (var first = _connection.BeginTransaction())
        {
            CashShopAaPointPurchaseStore.Stage(_connection, first, Commit(cash: 1, points: 100, ratio: 100));
            first.Commit();
        }
        using (var second = _connection.BeginTransaction())
        {
            var result = CashShopAaPointPurchaseStore.Stage(_connection, second,
                Commit(cash: 1, points: 100, ratio: 100));
            second.Commit();

            await Assert.That(result.Succeeded).IsTrue();
        }

        // Two real checkouts are two real charges and two ledger rows; the ledger is a record of
        // what happened, not a lock that refuses a second legitimate purchase.
        await Assert.That(ReadWallet("money")).IsEqualTo(StartMoney - 2);
        await Assert.That(ReadWallet("aa_point")).IsEqualTo(StartAaPoints + 200);
        await Assert.That(CountAudit()).IsEqualTo(2);
    }

    private static CashShopAaPointPurchaseCommit Commit(long cash, long points, uint ratio) =>
        new(AccountId, CharacterId, PurchaseDate, cash, points, ratio);

    private long ReadWallet(string column)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM characters WHERE id=@id";
        command.Parameters.AddWithValue("@id", CharacterId);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private int CountAudit()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM audit_ics_aa_point_purchases";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private long ReadAuditInt(string column)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM audit_ics_aa_point_purchases";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private void DropAuditTable()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DROP TABLE audit_ics_aa_point_purchases";
        command.ExecuteNonQuery();
    }

    private void SetDeleted()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "UPDATE characters SET deleted=1 WHERE id=@id";
        command.Parameters.AddWithValue("@id", CharacterId);
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
