using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>Outcome of one account-return claim attempt.</summary>
public enum AccountReturnClaimResult
{
    /// <summary>Ledger row inserted and the grant committed with it.</summary>
    Claimed,
    /// <summary>The account already holds a claim row; nothing was granted.</summary>
    AlreadyClaimed,
    /// <summary>Content days are not met (or the last sighting is unknown); nothing was granted.</summary>
    NotEligible,
    /// <summary>Content ships no reward item type; the feature is off. Loud, never a fallback.</summary>
    NoRewardConfigured,
    /// <summary>The grant failed and the claim rolled back, so the claim can be retried.</summary>
    GrantFailed,
}

/// <summary>
/// Account-return reward claims: eligibility from content days, and an exactly-once claim backed by
/// the <c>account_return_claims</c> primary key. The claim transaction inserts the ledger row first and
/// runs the caller's grant on the same connection/transaction, so the grant commits atomically with the
/// ledger row and a duplicate primary key refuses the second claim before anything is staged.
/// </summary>
public class AccountReturnManager : Singleton<AccountReturnManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Func<MySqlConnection> _connectionFactory;

    public AccountReturnManager()
        : this(MySQL.CreateConnection)
    {
    }

    /// <summary>Tests drive an isolated schema through this.</summary>
    public AccountReturnManager(Func<MySqlConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <summary>When the account was last seen: <c>accounts.last_login</c>, read fresh on every use
    /// so the value survives a relog instead of living in connection state.</summary>
    public DateTime? GetLastSeenUtc(uint accountId)
    {
        try
        {
            using var connection = _connectionFactory();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT last_login FROM accounts WHERE account_id=@account_id";
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Prepare();
            var result = command.ExecuteScalar();
            return result is null or DBNull
                ? null
                : ServerCalendar.AsUtc(Convert.ToDateTime(result));
        }
        catch (Exception e)
        {
            Logger.Error(e, "GetLastSeenUtc failed for account {0}", accountId);
            return null;
        }
    }

    /// <summary>Whether this account already consumed its return reward.</summary>
    public bool IsClaimed(uint accountId)
    {
        try
        {
            using var connection = _connectionFactory();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM account_return_claims WHERE account_id=@account_id";
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Prepare();
            return Convert.ToInt64(command.ExecuteScalar() ?? 0L) > 0;
        }
        catch (Exception e)
        {
            Logger.Error(e, "IsClaimed failed");
            return false;
        }
    }

    /// <summary>
    /// The sighting that decides eligibility. A qualifying absence is written on the account so a
    /// later login, which stamps last_login again on disconnect, still sees it. A newer qualifying
    /// sighting replaces an older one, so an absence that has aged out does not hide the next one.
    /// </summary>
    private DateTime? ResolveSighting(uint accountId, DateTime? lastSeenUtc)
    {
        var fresh = lastSeenUtc ?? GetLastSeenUtc(accountId);
        if (fresh is DateTime seen && ReturnAccountRules.IsEligible(seen, DateTime.UtcNow))
        {
            RememberQualifying(accountId, seen);
            return seen;
        }

        return ReadQualifying(accountId) ?? fresh;
    }

    private void RememberQualifying(uint accountId, DateTime seen)
    {
        try
        {
            using var connection = _connectionFactory();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE accounts
                SET return_qualifying_login = @seen
                WHERE account_id = @account_id
                  AND (return_qualifying_login IS NULL OR return_qualifying_login < @seen)
                """;
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Parameters.AddWithValue("@seen", ServerCalendar.AsUtc(seen));
            command.ExecuteNonQuery();
        }
        catch (Exception e)
        {
            Logger.Error(e, "RememberQualifying failed");
        }
    }

    private DateTime? ReadQualifying(uint accountId)
    {
        try
        {
            using var connection = _connectionFactory();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT return_qualifying_login FROM accounts WHERE account_id=@account_id";
            command.Parameters.AddWithValue("@account_id", accountId);
            var result = command.ExecuteScalar();
            return result is null or DBNull ? null : ServerCalendar.AsUtc(Convert.ToDateTime(result));
        }
        catch (Exception e)
        {
            Logger.Error(e, "ReadQualifying failed");
            return null;
        }
    }

    /// <summary>The status <c>SCReturnAccountStatus</c> carries: a reward is configured, the content
    /// days allow it and this account has not claimed it yet.</summary>
    public bool IsRewardAvailable(uint accountId, DateTime? lastSeenUtc = null)
    {
        if (!ReturnAccountRules.HasReward)
            return false;
        var lastSeen = ResolveSighting(accountId, lastSeenUtc);
        if (lastSeen is not { } seen)
            return false;
        if (!ReturnAccountRules.IsEligible(seen, DateTime.UtcNow))
            return false;
        return !IsClaimed(accountId);
    }

    /// <summary>
    /// Claims the account-return reward exactly once. The grant callback runs on the same transaction
    /// as the ledger insert: commit makes both visible together, and any failure rolls both back.
    /// </summary>
    public AccountReturnClaimResult TryClaim(
        uint accountId,
        Func<MySqlConnection, MySqlTransaction, bool> grantOn,
        DateTime? lastSeenUtc = null)
    {
        if (!ReturnAccountRules.HasReward)
        {
            Logger.Info(
                "Account {0} return claim skipped: content_configs '{1}' ships no reward item type",
                accountId, ReturnAccountRules.RewardItemTypeKey);
            return AccountReturnClaimResult.NoRewardConfigured;
        }

        var lastSeen = ResolveSighting(accountId, lastSeenUtc);
        if (lastSeen is not { } seen)
        {
            Logger.Info("Account {0} return claim refused: no last-seen timestamp", accountId);
            return AccountReturnClaimResult.NotEligible;
        }

        var now = DateTime.UtcNow;
        if (!ReturnAccountRules.IsEligible(seen, now))
        {
            Logger.Info(
                "Account {0} return claim refused: {1} days since sighting (rest={2}, block={3})",
                accountId,
                ReturnAccountRules.DaysSinceSighting(seen, now),
                ReturnAccountRules.RestDays,
                ReturnAccountRules.RewardBlockDays);
            return AccountReturnClaimResult.NotEligible;
        }

        MySqlConnection connection = null;
        MySqlTransaction transaction = null;
        try
        {
            connection = _connectionFactory();
            transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO account_return_claims (account_id, claimed_at, reward_item_type) " +
                    "VALUES (@account_id, @claimed_at, @reward_item_type)";
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Parameters.AddWithValue("@claimed_at", ServerCalendar.AsUtc(now));
                command.Parameters.AddWithValue("@reward_item_type", (uint)ReturnAccountRules.RewardItemType);
                command.Prepare();
                if (command.ExecuteNonQuery() != 1)
                {
                    transaction.Rollback();
                    Logger.Info("Account {0} return claim refused: ledger insert took no row", accountId);
                    return AccountReturnClaimResult.AlreadyClaimed;
                }
            }

            if (grantOn != null && !grantOn(connection, transaction))
            {
                transaction.Rollback();
                Logger.Error("Account {0} return claim rolled back: the reward grant failed", accountId);
                return AccountReturnClaimResult.GrantFailed;
            }

            transaction.Commit();
            return AccountReturnClaimResult.Claimed;
        }
        catch (MySqlException e) when (e.Number == 1062)
        {
            // Duplicate primary key: a concurrent or earlier claim owns the row.
            TryRollback(transaction);
            Logger.Info("Account {0} return claim refused: already claimed", accountId);
            return AccountReturnClaimResult.AlreadyClaimed;
        }
        catch (Exception e)
        {
            TryRollback(transaction);
            Logger.Error(e, "Account {0} return claim failed", accountId);
            return AccountReturnClaimResult.GrantFailed;
        }
        finally
        {
            transaction?.Dispose();
            connection?.Dispose();
        }
    }

    private static void TryRollback(MySqlTransaction transaction)
    {
        try
        {
            transaction?.Rollback();
        }
        catch (Exception e)
        {
            Logger.Error(e, "Return claim rollback failed");
        }
    }
}
