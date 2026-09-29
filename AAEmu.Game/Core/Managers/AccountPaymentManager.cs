using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>Outcome of one account payment/tier load.</summary>
public enum AccountPaymentLoadResult
{
    /// <summary>The account's row was found and applied to the connection's payment state.</summary>
    Loaded,
    /// <summary>The account owns no <c>account_payments</c> row. Entitlement is refused, loudly.</summary>
    NoRecord,
    /// <summary>The row could not be read, or names a method that is not a known payment method.</summary>
    Unreadable,
}

/// <summary>
/// Loads the account's payment tier during the 10.x connection path.
/// </summary>
/// <remarks>
/// The tier used to be a fixed premium subscription asserted in code, so every account read as paid
/// no matter what it had bought and every paid entitlement keyed off it. This reads it from the
/// account's <c>account_payments</c> row instead. A missing or unreadable row is never treated as a
/// paid tier: the result says so, and the connection is left on the no-entitlement state.
/// </remarks>
public class AccountPaymentManager : Singleton<AccountPaymentManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Func<MySqlConnection> _connectionFactory;

    public AccountPaymentManager()
        : this(MySQL.CreateConnection)
    {
    }

    /// <summary>Tests drive this with a connection factory they control.</summary>
    public AccountPaymentManager(Func<MySqlConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <summary>
    /// Reads the account's row and applies it to <paramref name="payment"/>. The payment is cleared
    /// first, so a failed load leaves a deterministic free tier rather than whatever was there before.
    /// </summary>
    public AccountPaymentLoadResult Load(uint accountId, AccountPayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        payment.Clear();

        AccountPaymentRecord record;
        try
        {
            if (!TryReadRecord(accountId, out record))
            {
                Logger.Error(
                    "Account {0} has no account_payments row; paid entitlements are refused. " +
                    "Run the account_payments migration, or insert the account's tier.",
                    accountId);
                return AccountPaymentLoadResult.NoRecord;
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Account payment load failed for account {0}; paid entitlements are refused",
                accountId);
            return AccountPaymentLoadResult.Unreadable;
        }

        if (!Enum.IsDefined(record.Method))
        {
            Logger.Error(
                "Account {0} has payment_method {1}, which is not a known payment method; " +
                "paid entitlements are refused.",
                accountId, (int)record.Method);
            return AccountPaymentLoadResult.Unreadable;
        }

        record = ApplySeededTier(accountId, record);
        payment.Apply(record);
        return AccountPaymentLoadResult.Loaded;
    }

    /// <summary>
    /// Replaces a placeholder row with the tier this server is configured to seed, once, and persists
    /// it. Any other row is returned untouched.
    /// </summary>
    /// <remarks>
    /// This exists because a SQL migration cannot read the server's configuration. The migration seeds
    /// a placeholder for every account that had none, and a placeholder written as the free tier is
    /// the "every existing account drops to free" fault. Upgrading on load puts the tier and its
    /// window in one place, so a migration cannot carry a duration that quietly disagrees with the
    /// configuration or that expires on its own.
    /// <para>
    /// A failure to persist is not fatal here: the grant is still applied for this session, so the
    /// account is not left free because a write failed, and the next login tries again.
    /// </para>
    /// </remarks>
    private AccountPaymentRecord ApplySeededTier(uint accountId, AccountPaymentRecord record)
    {
        if (!record.IsUntouchedSeededDefault)
            return record;

        var (method, days) = ConfiguredSeed();
        var grant = AccountManager.SeededPaymentGrant(accountId, method, days, DateTime.UtcNow);
        if (!TrySave(accountId, grant))
        {
            Logger.Warn(
                "Account {0} keeps the seeded tier for this session only: the account_payments row " +
                "could not be written, so the next login will seed it again.", accountId);
        }
        else
        {
            Logger.Info(
                "Account {0} held the placeholder payment row; seeded it to {1} until {2:u}.",
                accountId, grant.Method, grant.EndTime);
        }

        return grant;
    }

    /// <summary>
    /// The configured seeded tier and window length. Overridable so the upgrade is testable without
    /// the configuration singleton, which is not seeded in unit tests.
    /// </summary>
    protected virtual (PaymentMethodType Method, int Days) ConfiguredSeed()
    {
        var account = AppConfiguration.Instance?.Account;
        if (account == null)
            return (PaymentMethodType.None, 0);

        return (AccountManager.ParseSeededPaymentMethod(account.SeededPaymentMethod), account.SeededPaymentDays);
    }

    /// <summary>
    /// The account's persisted row, or <see langword="false"/> when it owns none. Overridable so the
    /// decision table above is testable without a database.
    /// </summary>
    protected virtual bool TryReadRecord(uint accountId, out AccountPaymentRecord record)
    {
        using var connection = _connectionFactory();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT payment_method, payment_location, pay_start, pay_end, buy_count " +
            "FROM account_payments WHERE account_id=@account_id";
        command.Parameters.AddWithValue("@account_id", accountId);
        command.Prepare();

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            record = null;
            return false;
        }

        record = new AccountPaymentRecord
        {
            AccountId = accountId,
            // Cast unchecked: a method value the enum does not define must reach the caller's
            // IsDefined check as itself, not be coerced into a defined one.
            Method = (PaymentMethodType)reader.GetInt32("payment_method"),
            Location = reader.GetInt32("payment_location"),
            StartTime = reader.GetDateTime("pay_start"),
            EndTime = reader.GetDateTime("pay_end"),
            BuyPremiumCount = reader.GetInt32("buy_count"),
        };
        return true;
    }

    /// <summary>
    /// Persists the account's tier. This is the write side of the same row the connection path
    /// reads, so a granted subscription survives a relog instead of living in session state.
    /// </summary>
    /// <remarks>Overridable so the seeding upgrade is testable without a database.</remarks>
    public virtual bool TrySave(uint accountId, AccountPaymentRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.AccountId != accountId)
            throw new InvalidOperationException(
                $"Payment record for account {record.AccountId} cannot be saved under account {accountId}.");

        try
        {
            using var connection = _connectionFactory();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO account_payments " +
                "(account_id, payment_method, payment_location, pay_start, pay_end, buy_count) " +
                "VALUES (@account_id, @method, @location, @start, @end, @buy_count) " +
                "ON DUPLICATE KEY UPDATE payment_method=VALUES(payment_method), " +
                "payment_location=VALUES(payment_location), pay_start=VALUES(pay_start), " +
                "pay_end=VALUES(pay_end), buy_count=VALUES(buy_count)";
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Parameters.AddWithValue("@method", (int)record.Method);
            command.Parameters.AddWithValue("@location", record.Location);
            command.Parameters.AddWithValue("@start", ServerCalendar.AsUtc(record.StartTime));
            command.Parameters.AddWithValue("@end", ServerCalendar.AsUtc(record.EndTime));
            command.Parameters.AddWithValue("@buy_count", record.BuyPremiumCount);
            command.Prepare();
            command.ExecuteNonQuery();
            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e, "Account payment save failed for account {0}", accountId);
            return false;
        }
    }
}
