using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Account;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Manages Connections and Game Account settings
/// </summary>
public class AccountManager : Singleton<AccountManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private ConcurrentDictionary<uint, GameConnection> _accounts;
    private readonly Dictionary<uint, object> _locks = new();

    public AccountManager()
    {
        _accounts = new ConcurrentDictionary<uint, GameConnection>();
        TickManager.Instance.OnTick.Subscribe(RemoveDeadConnections, TimeSpan.FromSeconds(30));
    }

    public void Add(GameConnection connection)
    {
        if (_accounts.ContainsKey(connection.AccountId))
            return;
        _accounts.TryAdd(connection.AccountId, connection);
        var lastLogin = UpdateLoginTime(connection.AccountId, DateTime.UtcNow);
        var accountDetails = GetAccountDetails(connection.AccountId);
        if (lastLogin < DateTime.UtcNow.Date)
        {
            // Logged in for a new day
            TimedRewardsManager.Instance.DoDailyAccountLogin(connection.AccountId);
        }
        // Add offline labor
        TimedRewardsManager.Instance.AddOfflineLabor(connection, lastLogin, accountDetails.Labor);
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

            // Account didn't exist, check if it's out first
            command.CommandText = "SELECT COUNT(*) FROM accounts";
            command.Prepare();
            var accountCount = (int)(command.ExecuteScalar() ?? 0);
            var newAccessLevel = (accountCount <= 0)
                ? AppConfiguration.Instance.Account.AccessLevelFirstAccount
                : 0;

            command.CommandText = "INSERT INTO accounts (account_id, access_level, labor, credits, loyalty) VALUES (@acc_id, @access_level, @labor, @credits, @loyalty)";
            command.Parameters.AddWithValue("@access_level", newAccessLevel);
            command.Parameters.AddWithValue("@labor", AppConfiguration.Instance.Labor.Default);
            command.Parameters.AddWithValue("@credits", AppConfiguration.Instance.Credits.Default);
            command.Parameters.AddWithValue("@loyalty", AppConfiguration.Instance.Loyalty.Default);
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

    public bool RemoveCredits(uint accountId, int credits) => AddCredits(accountId, -credits);
    
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
}
