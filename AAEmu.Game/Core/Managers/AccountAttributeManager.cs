using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Models.Game;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>One granted account attribute.</summary>
public class AccountAttribute
{
    public uint AccountId { get; init; }
    public uint KindId { get; init; }
    public uint KindValue { get; init; }

    /// <summary>Shard this entry is confined to, 0 when it applies account-wide.</summary>
    public uint WorldId { get; init; }

    public int Count { get; set; }

    /// <summary>When the current grant period began.</summary>
    public DateTime Starts { get; set; }

    /// <summary>When the entry lapses; DateTime.MaxValue for a permanent one.</summary>
    public DateTime Expires { get; set; }

    public bool IsExpired => Expires <= DateTime.UtcNow;
}

/// <summary>
/// Account-scoped attributes — the things an account owns rather than a character does.
/// <c>enum_account_attribute_kinds</c> is 1 auction_post, 2 account_buff, 3 ulc.
///
/// This exists because there was nowhere to put them. Every other store in the server is keyed by character,
/// and <c>account_attribute_effects.bind_world</c> explicitly distinguishes an entry confined to one shard
/// from one that follows the account everywhere, which a character row cannot express: granting a per-account
/// buff on one character would leave the others without it.
/// </summary>
public class AccountAttributeManager : Singleton<AccountAttributeManager>, IAccountAttributeManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<uint, List<AccountAttribute>> _byAccount = [];
    private readonly object _lock = new();

    public void Load()
    {
        lock (_lock)
        {
            _byAccount.Clear();

            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM account_attributes";
            command.Prepare();

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var attribute = new AccountAttribute
                {
                    AccountId = reader.GetUInt32("account_id"),
                    KindId = reader.GetUInt32("kind_id"),
                    KindValue = reader.GetUInt32("kind_value"),
                    WorldId = reader.GetUInt32("world_id"),
                    Count = reader.GetInt32("count"),
                    Starts = reader.GetDateTime("starts"),
                    Expires = reader.GetDateTime("expires")
                };

                if (!_byAccount.TryGetValue(attribute.AccountId, out var list))
                {
                    list = [];
                    _byAccount.Add(attribute.AccountId, list);
                }

                list.Add(attribute);
            }
        }

        Logger.Info($"Loaded account attributes for {_byAccount.Count} accounts");
    }

    /// <summary>Live entries for an account on a shard, skipping anything lapsed or bound to another shard.</summary>
    public List<AccountAttribute> Get(uint accountId, uint worldId)
    {
        lock (_lock)
        {
            if (!_byAccount.TryGetValue(accountId, out var list))
                return [];

            return list.Where(a =>
                !a.IsExpired &&
                (a.WorldId == 0 || a.WorldId == worldId) &&
                a.KindId <= byte.MaxValue &&
                Enum.IsDefined((AccountAttributeKind)(byte)a.KindId)).ToList();
        }
    }

    public AccountAttribute Find(uint accountId, uint kindId, uint kindValue, uint worldId)
    {
        lock (_lock)
        {
            return _byAccount.TryGetValue(accountId, out var list)
                ? list.FirstOrDefault(a => a.KindId == kindId && a.KindValue == kindValue && a.WorldId == worldId)
                : null;
        }
    }

    /// <summary>
    /// Grants or removes an account attribute. A count of zero is a presence-only attribute: shipped
    /// account_attribute_effects use that form for every account buff and ULC grant. On removal, zero removes
    /// the whole matching attribute; a positive count consumes that many. A duration of zero is permanent.
    /// Returns the entry as it stands afterwards, or null after removal.
    /// </summary>
    public AccountAttribute Change(uint accountId, uint kindId, uint kindValue, uint worldId, bool isAdd, int count, int durationMinutes)
    {
        lock (_lock)
        {
            if (!_byAccount.TryGetValue(accountId, out var list))
            {
                list = [];
                _byAccount.Add(accountId, list);
            }

            var existing = list.FirstOrDefault(a => a.KindId == kindId && a.KindValue == kindValue && a.WorldId == worldId);

            if (isAdd && existing == null)
            {
                existing = new AccountAttribute
                {
                    AccountId = accountId,
                    KindId = kindId,
                    KindValue = kindValue,
                    WorldId = worldId,
                    Count = 0,
                    Starts = DateTime.UtcNow,
                    Expires = DateTime.MaxValue
                };

                list.Add(existing);
            }

            if (!isAdd)
            {
                if (existing == null)
                    return null;

                if (count <= 0 || existing.Count <= count)
                {
                    list.Remove(existing);
                    return null;
                }

                existing.Count -= count;
                return existing;
            }

            existing!.Count += count;

            // A refresh restarts the clock rather than stacking durations, which is how the client shows a
            // reapplied account buff.
            if (durationMinutes > 0)
            {
                existing.Starts = DateTime.UtcNow;
                existing.Expires = existing.Starts.AddMinutes(durationMinutes);
            }

            return existing;
        }
    }

    public (int, int) Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        var saved = 0;

        lock (_lock)
        {
            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM account_attributes";
                deleteCommand.Prepare();
                deleteCommand.ExecuteNonQuery();
            }

            foreach (var attribute in _byAccount.Values.SelectMany(list => list))
            {
                if (attribute.IsExpired)
                    continue;

                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    "INSERT INTO account_attributes (account_id, kind_id, kind_value, world_id, count, starts, expires) " +
                    "VALUES (@account_id, @kind_id, @kind_value, @world_id, @count, @starts, @expires)";
                command.Parameters.AddWithValue("@account_id", attribute.AccountId);
                command.Parameters.AddWithValue("@kind_id", attribute.KindId);
                command.Parameters.AddWithValue("@kind_value", attribute.KindValue);
                command.Parameters.AddWithValue("@world_id", attribute.WorldId);
                command.Parameters.AddWithValue("@count", attribute.Count);
                command.Parameters.AddWithValue("@starts", attribute.Starts);
                command.Parameters.AddWithValue("@expires", attribute.Expires);
                command.Prepare();
                command.ExecuteNonQuery();
                saved++;
            }
        }

        return (saved, 0);
    }
}
