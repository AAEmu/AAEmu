using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Account;
using AAEmu.Game.Models.Game;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Manages Connections and Game Account settings
/// </summary>
public class AccountManager : Singleton<AccountManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private ConcurrentDictionary<ulong, GameConnection> _accounts;
    private readonly Dictionary<ulong, object> _locks = new();

    public AccountManager()
    {
        _accounts = new ConcurrentDictionary<ulong, GameConnection>();
        TickManager.Instance.OnTick.Subscribe(RemoveDeadConnections, TimeSpan.FromSeconds(30));
    }

    public void Add(GameConnection connection)
    {
        if (_accounts.ContainsKey(connection.AccountId))
            return;
        _accounts.TryAdd(connection.AccountId, connection);
        var lastLogin = UpdateLoginTime(connection.AccountId, DateTime.UtcNow);
        var accountDetails = GetAccountDetails(connection.AccountId);

        var lsi = GetDivineClock(connection.AccountId);
        if (lsi?.Count == 0)
        {
            TimedRewardsManager.Instance.DoDailyAccountDivineClockLogin(connection.AccountId);
        }

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

    public void Remove(ulong id)
    {
        _accounts.TryRemove(id, out _);
    }

    public bool Contains(ulong id)
    {
        return _accounts.ContainsKey(id);
    }

    public int Count() => _accounts.Count; 

    private AccountDetails GetAccountDetailsInternal(ulong accountId)
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
                res.AccountId = reader.GetUInt64("account_id");
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

            // Account didn't exist, check if it's our first
            command.CommandText = "SELECT COUNT(*) FROM accounts";
            command.Prepare();
            var accountCount = (int)((long)(command.ExecuteScalar() ?? 0L));
            var newAccessLevel = (accountCount <= 0)
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
            res.AccountId = (ulong)command.LastInsertedId;
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

    public AccountDetails GetAccountDetails(ulong accountId)
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

    public bool AddCredits(ulong accountId, int creditsAmount)
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

    public bool RemoveCredits(ulong accountId, int credits) => AddCredits(accountId, -credits);

    public bool AddLoyalty(ulong accountId, int loyaltyAmount)
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

    public void UpdateLabor(ulong accountId, short laborPower)
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
    public DateTime UpdateLoginTime(ulong accountId, DateTime newTime)
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
    public void UpdateTickTimes(ulong accountId, DateTime newTime, bool updateLabor, bool updateCredits, bool updateLoyalty)
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
    public void UpdateDivineClock(ulong accountId, uint timeElapsed, uint timesTaken)
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
                command.CommandText = "UPDATE accounts SET cumulated = @cumulated , gave = @gave" +
                                      " WHERE account_id = @account_id";
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Parameters.AddWithValue("@cumulated", timeElapsed);
                command.Parameters.AddWithValue("@gave", timesTaken);
                command.Prepare();
                command.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
            }
        }
    }

    public void UpdateDivineClock(ulong accountId, uint scheduleItemId, uint timeElapsed, uint timesTaken)
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
                command.CommandText = "UPDATE divine_clock SET cumulated = @cumulated, gave = @gave" +
                                      " WHERE account_id = @account_id AND schedule_item_id = @schedule_item_id";
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Parameters.AddWithValue("@schedule_item_id", scheduleItemId);
                command.Parameters.AddWithValue("@cumulated", timeElapsed);
                command.Parameters.AddWithValue("@gave", timesTaken);
                command.Prepare();
                var rowsAffected = command.ExecuteNonQuery();

                // If no rows were updated, insert a new record
                if (rowsAffected == 0)
                {
                    command.CommandText = "INSERT INTO divine_clock (account_id, schedule_item_id, cumulated, gave)" +
                                          "VALUES (@account_id, @schedule_item_id, @cumulated, @gave)";
                    command.ExecuteNonQuery();
                }
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
    public (uint, uint) GetDivineClock2(ulong accountId)
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

    public (uint TimeElapsed, uint TimesTaken) GetDivineClock2(ulong accountId, uint scheduleItemId)
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
                command.CommandText = "SELECT cumulated, gave FROM divine_clock WHERE account_id = @account_id AND schedule_item_id = @schedule_item_id";
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Parameters.AddWithValue("@schedule_item_id", scheduleItemId);
                command.Prepare();
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    timeElapsed = reader.GetUInt32("cumulated");
                    timesTaken = reader.GetUInt32("gave");
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

    public ScheduleItem GetDivineClock(ulong accountId, uint scheduleItemId)
    {
        var scheduleItem = new ScheduleItem();
        scheduleItem.ScheduleItemId = scheduleItemId;
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
                command.CommandText = "SELECT cumulated, gave FROM divine_clock WHERE account_id = @account_id AND schedule_item_id = @schedule_item_id";
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Parameters.AddWithValue("@schedule_item_id", scheduleItemId);
                command.Prepare();
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    scheduleItem.Cumulated = reader.GetUInt32("cumulated");
                    scheduleItem.Gave = reader.GetByte("gave");
                }
                reader.Close();
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
                return null; // Возвращаем null в случае ошибки
            }
        }

        return scheduleItem;
    }
    public List<ScheduleItem> GetDivineClock(ulong accountId)
    {
        var scheduleItems = new List<ScheduleItem>();
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
                command.CommandText = "SELECT * FROM divine_clock WHERE account_id = @account_id";
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Prepare();

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var scheduleItem = new ScheduleItem
                    {
                        ScheduleItemId = reader.GetUInt32("schedule_item_id"),
                        Cumulated = reader.GetUInt32("cumulated"),
                        Gave = reader.GetByte("gave")
                    };
                    scheduleItems.Add(scheduleItem);
                }
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
                return null; // Возвращаем null в случае ошибки
            }
        }

        return scheduleItems;
    }

    public bool DeleteDivineClockEntries(ulong accountId)
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
                command.CommandText = "DELETE FROM divine_clock WHERE account_id = @account_id";
                command.Parameters.AddWithValue("@account_id", accountId);
                command.Prepare();

                int rowsAffected = command.ExecuteNonQuery();
                return rowsAffected > 0; // Возвращаем true, если были удалены записи
            }
            catch (Exception e)
            {
                Logger.Error($"{e.Message}\n{e.StackTrace}");
                return false; // Возвращаем false в случае ошибки
            }
        }
    }
}
