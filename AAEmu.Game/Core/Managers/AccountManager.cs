using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Account;
using AAEmu.Game.Models.Game.Char;
using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Manages Connections and Game Account settings
/// </summary>
public class AccountManager(ITickManager tickManager, ITimedRewardsManager timedRewardsManager) : Singleton<AccountManager>, IAccountManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<uint, GameConnection> _accounts = new();
    private readonly Dictionary<uint, object> _locks = [];

    /// <summary>
    /// Serializes an account operation with all existing balance writers. Farmhand callers hold this around
    /// Butler locks and their transaction: gate, account, Butler operation/state, database.
    /// </summary>
    public T WithAccountLock<T>(uint accountId, Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using var scope = EnterAccountLock(accountId);
        return operation();
    }

    /// <summary>
    /// Takes the same lock <see cref="WithAccountLock{T}"/> takes and holds it until the returned scope is
    /// disposed, for a caller that already owns the enclosing scopes of the documented lock order (the World
    /// persistence gate and the character state lock) and therefore cannot wrap its whole body in a lambda.
    /// </summary>
    internal IDisposable EnterAccountLock(uint accountId)
    {
        var accountLock = GetAccountLock(accountId);
        Monitor.Enter(accountLock);
        return new AccountLockScope(accountLock);
    }

    /// <summary>True when this thread already holds the account lock for <paramref name="accountId"/>.</summary>
    internal bool IsAccountLockHeld(uint accountId) => Monitor.IsEntered(GetAccountLock(accountId));

    private sealed class AccountLockScope(object accountLock) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Monitor.Exit(accountLock);
        }
    }

    /// <summary>
    /// Writes an account-first debit only if both persisted labor pools still match the caller's locked snapshot.
    /// The caller must already hold <see cref="WithAccountLock{T}"/> for this account and commit its transaction
    /// before applying or publishing the returned debit. A <see langword="false"/> result leaves caches unchanged;
    /// the caller rolls back and retries from a fresh account snapshot.
    /// </summary>
    public bool TryDebitLaborOn(
        AccountLaborDebit debit,
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        if (connection == null || transaction == null || debit.AccountId == 0 ||
            !IsAccountLockHeld(debit.AccountId))
            return false;

        try
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "UPDATE accounts SET labor=@new_labor, local_labor=@new_local_labor " +
                "WHERE account_id=@account_id AND labor=@expected_labor AND local_labor=@expected_local_labor";
            command.Parameters.AddWithValue("@account_id", debit.AccountId);
            command.Parameters.AddWithValue("@expected_labor", debit.Before.Labor);
            command.Parameters.AddWithValue("@expected_local_labor", debit.Before.LocalLabor);
            command.Parameters.AddWithValue("@new_labor", debit.After.Labor);
            command.Parameters.AddWithValue("@new_local_labor", debit.After.LocalLabor);
            command.Prepare();
            return command.ExecuteNonQuery() == 1;
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to debit account labor");
            return false;
        }
    }

    /// <summary>
    /// Applies a committed debit to the active character cache while the account lock is still held. The caller
    /// publishes the result only after it releases its Butler, account, and persistence-gate locks.
    /// </summary>
    public AccountLaborDebitPublication ApplyCommittedLaborDebit(Character character, AccountLaborDebit debit)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (character.AccountId != debit.AccountId || !Monitor.IsEntered(GetAccountLock(debit.AccountId)))
            throw new InvalidOperationException("A committed account labor debit must be applied under its account lock.");

        character.InitializeLaborCache(debit.After.Labor, debit.After.LocalLabor, character.LaborPowerModified);
        return new AccountLaborDebitPublication(character, debit);
    }

    /// <summary>Persists a direct account-pool cache assignment under the same lock used by farmhand debits.</summary>
    public bool TrySetCharacterLabor(Character character, short labor)
    {
        ArgumentNullException.ThrowIfNull(character);
        return WithAccountLock(character.AccountId, () =>
        {
            try
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE accounts SET labor=@labor WHERE account_id=@account_id";
                command.Parameters.AddWithValue("@account_id", character.AccountId);
                command.Parameters.AddWithValue("@labor", labor);
                command.Prepare();
                if (command.ExecuteNonQuery() != 1)
                    return false;
                character.InitializeLaborCache(labor, character.LocalLaborPower, character.LaborPowerModified);
                return true;
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to update account labor");
                return false;
            }
        });
    }

    /// <summary>Persists a direct server-local labor cache assignment under the shared account lock.</summary>
    public bool TrySetCharacterLocalLabor(Character character, int localLabor)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (localLabor < 0)
            return false;
        return WithAccountLock(character.AccountId, () =>
        {
            try
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE accounts SET local_labor=@local_labor WHERE account_id=@account_id";
                command.Parameters.AddWithValue("@account_id", character.AccountId);
                command.Parameters.AddWithValue("@local_labor", localLabor);
                command.Prepare();
                if (command.ExecuteNonQuery() != 1)
                    return false;
                character.InitializeLaborCache(character.LaborPower, localLabor, character.LaborPowerModified);
                return true;
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to update server-local labor");
                return false;
            }
        });
    }

    public void Initialize()
    {
        tickManager.OnTick.Subscribe(RemoveDeadConnections, TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// The premium grade the ACCOUNT is at, for every path that runs without a character selected.
    /// </summary>
    /// <remarks>
    /// Premium is account-wide, so the best point total any living character on it reached stands in
    /// for the account.
    ///
    /// The character list cannot be the source here. The offline catch-up runs from <see cref="Add"/>,
    /// which happens at authentication, while <c>GameConnection.LoadAccount</c> only fills
    /// <c>Characters</c> later at CSAesXorKey - so reading the list would resolve every paid account as
    /// the free tier for the single largest labor credit it ever gets. The loaded list is used when it
    /// is there, to save a query, and the database answers when it is not.
    ///
    /// One method for lobby and reward tick both: the lobby tells the player which tier they are on and
    /// the tick decides what that tier pays, and those two answering differently is how an account ends
    /// up shown one thing and paid another.
    /// </remarks>
    public (int Point, uint Grade) GetAccountPremium(GameConnection connection)
    {
        var point = 0;
        if (connection?.Characters is { Count: > 0 })
        {
            foreach (var character in connection.Characters.Values)
                point = Math.Max(point, character.Point);
        }
        else if (connection != null)
        {
            point = GetMaxCharacterPoint(connection.AccountId);
        }

        point = AccountPatron.ResolvePoint(point);
        return (point, AccountPatron.ResolveGrade(PremiumGameData.Instance.GetGradeForPoint(point)));
    }

    /// <summary>
    /// Highest <c>characters.point</c> on the account, straight from the database.
    /// </summary>
    private static int GetMaxCharacterPoint(uint accountId)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COALESCE(MAX(`point`), 0) FROM `characters` WHERE `account_id`=@account_id AND `deleted`=0";
            command.Parameters.AddWithValue("@account_id", accountId);
            command.Prepare();
            var result = command.ExecuteScalar();
            return result is null or DBNull ? 0 : Convert.ToInt32(result);
        }
        catch (Exception e)
        {
            // The free tier is the safe answer: it under-pays rather than handing out a grade the
            // account may not hold, and it says so instead of failing silently.
            Logger.Error(e, "GetMaxCharacterPoint failed; assuming no premium");
            return 0;
        }
    }

    public void Add(GameConnection connection)
    {
        if (_accounts.ContainsKey(connection.AccountId))
            return;
        _accounts.TryAdd(connection.AccountId, connection);
        var lastLogin = UpdateLoginTime(connection.AccountId, DateTime.UtcNow);
        connection.PreviousLoginUtc = lastLogin;
        connection.HasPreviousLogin = true;
        var accountDetails = GetAccountDetails(connection.AccountId);
        if (lastLogin < DateTime.UtcNow.Date)
        {
            // Logged in for a new day
            timedRewardsManager.DoDailyAccountLogin(connection.AccountId);
        }
        // Add offline labor
        timedRewardsManager.AddOfflineLabor(connection, lastLogin, accountDetails.Labor);
    }

    private void RemoveDeadConnections(TimeSpan delta)
    {
        foreach (var gameConnection in _accounts.Values.ToList().Where(gameConnection => gameConnection.LastPing + TimeSpan.FromSeconds(30) < DateTime.UtcNow))
        {
            if (gameConnection.ActiveChar != null)
                Logger.Trace($"Disconnecting {gameConnection.ActiveChar.Name} due to no network activity");
            gameConnection.Shutdown();
        }
    }

    public void Remove(uint id)
    {
        _accounts.TryRemove(id, out _);
    }

    public bool Contains(uint id)
    {
        return _accounts.ContainsKey(id);
    }

    public int Count() => _accounts.Count;

    private object GetAccountLock(uint accountId)
    {
        lock (_locks)
        {
            if (_locks.TryGetValue(accountId, out var accountLock))
                return accountLock;
            accountLock = new object();
            _locks.Add(accountId, accountLock);
            return accountLock;
        }
    }

    private AccountDetails GetAccountDetailsInternal(uint accountId)
    {
        var res = new AccountDetails();
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM accounts WHERE account_id = @acc_id";
            command.Parameters.AddWithValue("@acc_id", accountId);
            command.Prepare();
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                res.AccountId = reader.GetInt32("account_id");
                res.AccessLevel = reader.GetInt32("access_level");
                res.Labor = reader.GetInt16("labor");
                res.LocalLabor = (int)reader.GetUInt32("local_labor");
                res.Credits = reader.GetInt32("credits");
                res.Loyalty = reader.GetInt32("loyalty");
                res.LastUpdated = reader.GetDateTime("last_updated");
                res.LastLogin = reader.GetDateTime("last_login");
                res.LastLaborTick = reader.GetDateTime("last_labor_tick");
                res.LastCreditsTick = reader.GetDateTime("last_credits_tick");
                res.LastLoyaltyTick = reader.GetDateTime("last_loyalty_tick");
                return res;
            }

            reader.Close();

            // Account didn't exist, check if it's our first
            command.CommandText = "SELECT COUNT(*) FROM accounts";
            command.Prepare();
            var accountCount = (int)(long)(command.ExecuteScalar() ?? 0L);
            var newAccessLevel = accountCount <= 0
                ? AppConfiguration.Instance.Account.AccessLevelFirstAccount
                : 0;

            command.CommandText = "INSERT INTO accounts (account_id, access_level, labor, credits, loyalty, last_login, last_labor_tick, last_credits_tick, last_loyalty_tick) VALUES (@acc_id, @access_level, @labor, @credits, @loyalty, @last_login, @last_labor_tick, @last_credits_tick, @last_loyalty_tick)";
            command.Parameters.AddWithValue("@access_level", newAccessLevel);
            command.Parameters.AddWithValue("@labor", AppConfiguration.Instance.Labor.Default);
            command.Parameters.AddWithValue("@credits", AppConfiguration.Instance.Credits.Default);
            command.Parameters.AddWithValue("@loyalty", AppConfiguration.Instance.Loyalty.Default);
            command.Parameters.AddWithValue("@last_login", DateTime.UtcNow);
            command.Parameters.AddWithValue("@last_labor_tick", DateTime.UtcNow);
            command.Parameters.AddWithValue("@last_credits_tick", DateTime.UtcNow);
            command.Parameters.AddWithValue("@last_loyalty_tick", DateTime.UtcNow);
            command.Prepare();
            command.ExecuteNonQuery();
            res.AccountId = (int)command.LastInsertedId;
            res.LastLogin = DateTime.UtcNow;
            res.LastUpdated = DateTime.UtcNow;
            res.LastLaborTick = DateTime.UtcNow;
            res.LastCreditsTick = DateTime.UtcNow;
            res.LastLoyaltyTick = DateTime.UtcNow;
            return res;
        }
        catch (Exception e)
        {
            Logger.Error(e.Message);
            return res;
        }

    }

    public AccountDetails GetAccountDetails(uint accountId)
    {
        object accLock;
        lock (_locks)
        {
            if (!_locks.TryGetValue(accountId, out accLock))
            {
                accLock = new object();
                _locks.Add(accountId, accLock);
            }
        }
        lock (accLock)
        {
            try
            {
                return GetAccountDetailsInternal(accountId);
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
                return new AccountDetails();
            }
        }
    }

    public bool AddCredits(uint accountId, int creditsAmount)
    {
        object accLock;
        lock (_locks)
        {
            if (!_locks.TryGetValue(accountId, out accLock))
            {
                accLock = new object();
                _locks.Add(accountId, accLock);
            }
        }
        lock (accLock)
        {
            try
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO accounts (account_id, credits) VALUES(@acc_id, @credits_amount) ON DUPLICATE KEY UPDATE credits = credits + @credits_amount";
                command.Parameters.AddWithValue("@acc_id", accountId);
                command.Parameters.AddWithValue("@credits_amount", creditsAmount);
                command.Prepare();
                return command.ExecuteNonQuery() > 0;
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
                return false;
            }
        }
    }

    /// <summary>
    /// Same write as <see cref="AddCredits"/> on the caller's transaction so a claim and
    /// its cash reward commit or roll back together.
    /// </summary>
    public bool AddCreditsOn(uint accountId, int creditsAmount, MySqlConnection connection, MySqlTransaction transaction)
    {
        if (connection == null || transaction == null || creditsAmount == 0)
            return creditsAmount == 0;

        object accLock;
        lock (_locks)
        {
            if (!_locks.TryGetValue(accountId, out accLock))
            {
                accLock = new object();
                _locks.Add(accountId, accLock);
            }
        }

        lock (accLock)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.Connection = connection;
                command.Transaction = transaction;
                if (creditsAmount < 0)
                {
                    command.CommandText =
                        "UPDATE accounts SET credits = credits + @credits_amount WHERE account_id = @acc_id AND credits >= @need";
                    command.Parameters.AddWithValue("@need", -creditsAmount);
                }
                else
                {
                    command.CommandText =
                        "INSERT INTO accounts (account_id, credits) VALUES(@acc_id, @credits_amount) ON DUPLICATE KEY UPDATE credits = credits + @credits_amount";
                }

                command.Parameters.AddWithValue("@acc_id", accountId);
                command.Parameters.AddWithValue("@credits_amount", creditsAmount);
                command.Prepare();
                return creditsAmount < 0
                    ? command.ExecuteNonQuery() == 1
                    : command.ExecuteNonQuery() > 0;
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
                return false;
            }
        }
    }

    public bool RemoveCredits(uint accountId, int credits) => AddCredits(accountId, -credits);

    /// <summary>
    /// Same write as <see cref="AddLoyalty"/> on the caller's transaction so a convert
    /// and its bag removal commit or roll back together.
    /// </summary>
    public bool AddLoyaltyOn(uint accountId, int loyaltyAmount, MySqlConnection connection, MySqlTransaction transaction)
    {
        if (connection == null || transaction == null || loyaltyAmount == 0)
            return loyaltyAmount == 0;

        object accLock;
        lock (_locks)
        {
            if (!_locks.TryGetValue(accountId, out accLock))
            {
                accLock = new object();
                _locks.Add(accountId, accLock);
            }
        }

        lock (accLock)
        {
            try
            {
                using var command = connection.CreateCommand();
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO accounts (account_id, loyalty) VALUES(@acc_id, @loyalty_amount) ON DUPLICATE KEY UPDATE loyalty = loyalty + @loyalty_amount";
                command.Parameters.AddWithValue("@acc_id", accountId);
                command.Parameters.AddWithValue("@loyalty_amount", loyaltyAmount);
                command.Prepare();
                return command.ExecuteNonQuery() > 0;
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
                return false;
            }
        }
    }

    public bool AddLoyalty(uint accountId, int loyaltyAmount)
    {
        object accLock;
        lock (_locks)
        {
            if (!_locks.TryGetValue(accountId, out accLock))
            {
                accLock = new object();
                _locks.Add(accountId, accLock);
            }
        }
        lock (accLock)
        {
            try
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO accounts (account_id, loyalty) VALUES(@acc_id, @loyalty_amount) ON DUPLICATE KEY UPDATE loyalty = loyalty + @loyalty_amount";
                command.Parameters.AddWithValue("@acc_id", accountId);
                command.Parameters.AddWithValue("@loyalty_amount", loyaltyAmount);
                command.Prepare();
                return command.ExecuteNonQuery() > 0;
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
                return false;
            }
        }
    }

    public void UpdateLabor(uint accountId, short laborPower)
    {
        lock (GetAccountLock(accountId))
        {
            try
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE accounts SET labor = @labor WHERE account_id = @account_id";
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Parameters.AddWithValue("@labor", laborPower);
                command.Prepare();
                command.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
            }
        }
    }

    /// <summary>
    /// Persists the SERVER-LOCAL pool ("Online Labor"). Account-scoped, see
    /// <see cref="AccountDetails.LocalLabor"/>.
    /// </summary>
    public void UpdateLocalLabor(uint accountId, int localLabor)
    {
        lock (GetAccountLock(accountId))
        {
            try
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE accounts SET local_labor = @local_labor WHERE account_id = @account_id";
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Parameters.AddWithValue("@local_labor", Math.Max(0, localLabor));
                command.Prepare();
                command.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
            }
        }
    }

    /// <summary>
    /// Updates the login time to a new time and returns the old time
    /// </summary>
    /// <param name="accountId"></param>
    /// <param name="newTime"></param>
    /// <returns>Previous value for LastLogin</returns>
    public DateTime UpdateLoginTime(uint accountId, DateTime newTime)
    {
        object accLock;
        lock (_locks)
        {
            if (!_locks.TryGetValue(accountId, out accLock))
            {
                accLock = new object();
                _locks.Add(accountId, accLock);
            }
        }

        lock (accLock)
        {
            try
            {
                var res = GetAccountDetailsInternal(accountId);

                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE accounts SET last_login = @last_login WHERE account_id = @account_id";
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Parameters.AddWithValue("@last_login", newTime);
                command.Prepare();
                command.ExecuteNonQuery();

                return res.LastLogin;
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
                return DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Updates tick timer in DB, do not set more than one flag at a time
    /// </summary>
    /// <param name="accountId"></param>
    /// <param name="newTime"></param>
    /// <param name="updateLabor"></param>
    /// <param name="updateCredits"></param>
    /// <param name="updateLoyalty"></param>
    public void UpdateTickTimes(uint accountId, DateTime newTime, bool updateLabor, bool updateCredits, bool updateLoyalty)
    {
        object accLock;
        lock (_locks)
        {
            if (!_locks.TryGetValue(accountId, out accLock))
            {
                accLock = new object();
                _locks.Add(accountId, accLock);
            }
        }

        lock (accLock)
        {
            try
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                var updateFieldName = "error";
                if (updateLabor)
                    updateFieldName = "last_labor_tick";
                if (updateCredits)
                    updateFieldName = "last_credits_tick";
                if (updateLoyalty)
                    updateFieldName = "last_loyalty_tick";
                command.CommandText = $"UPDATE accounts SET {updateFieldName} = @new_time WHERE account_id = @account_id";
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Parameters.AddWithValue("@new_time", newTime);
                command.Prepare();
                command.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
            }
        }
    }

    /// <summary>
    /// Updates the divine_clock_time and divine_clock_taken values for given account
    /// </summary>
    /// <param name="accountId"></param>
    /// <param name="timeElapsed"></param>
    /// <param name="timesTaken"></param>
    public void UpdateDivineClock(uint accountId, uint timeElapsed, uint timesTaken)
    {
        object accLock;
        lock (_locks)
        {
            if (!_locks.TryGetValue(accountId, out accLock))
            {
                accLock = new object();
                _locks.Add(accountId, accLock);
            }
        }
        lock (accLock)
        {
            try
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE accounts SET divine_clock_time = @divine_clock_time , divine_clock_taken = @divine_clock_taken WHERE account_id = @account_id";
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Parameters.AddWithValue("@divine_clock_time", timeElapsed);
                command.Parameters.AddWithValue("@divine_clock_taken", timesTaken);
                command.Prepare();
                command.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
            }
        }
    }

    /// <summary>
    /// Returns the divine_clock_time and divine_clock_taken values for given account
    /// </summary>
    /// <param name="accountId"></param>
    /// <returns></returns>
    public (uint, uint) GetDivineClock(uint accountId)
    {
        var timeElapsed = 0u;
        var timesTaken = 0u;
        object accLock;
        lock (_locks)
        {
            if (!_locks.TryGetValue(accountId, out accLock))
            {
                accLock = new object();
                _locks.Add(accountId, accLock);
            }
        }
        lock (accLock)
        {
            try
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT account_id, divine_clock_time, divine_clock_taken FROM accounts WHERE account_id = @account_id";
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Prepare();
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    timeElapsed = reader.GetUInt32("divine_clock_time");
                    timesTaken = reader.GetUInt32("divine_clock_taken");
                }
                reader.Close();
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
            }
        }

        return (timeElapsed, timesTaken);
    }
}
