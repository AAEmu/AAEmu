using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.SecondPassword;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// The second password: the key tables a client reads its window from, and the password that comes back as
/// clicked positions. Both are per account, which is what the window itself is — the actions it guards are
/// account-wide (trading, destroying) rather than tied to the character that happens to be online.
/// </summary>
public class SecondPasswordManager : Singleton<SecondPasswordManager>
{
    /// <summary>
    /// How many wrong passwords an account may give before it is locked out. Left at zero, which means no
    /// lock-out: the limit and the lock duration are retail values we have not read anywhere, and inventing
    /// them would lock players out on a guess. Raise this once the real value is known.
    /// </summary>
    public const int MaxFailedAttempts = 0;

    private sealed record IssuedTables(uint Time, string[] Tables);

    private sealed class StoredPassword
    {
        public byte[] Salt { get; init; }
        public string Hash { get; set; }
        public int FailedCount { get; set; }
    }

    private readonly ConcurrentDictionary<uint, IssuedTables> _issued = new();
    private readonly ConcurrentDictionary<uint, StoredPassword> _passwords = new();

    /// <summary>The tables handed out for an account, and the time they were issued with.</summary>
    public string[] Issue(uint accountId, out uint time)
    {
        var tables = SecondPasswordKeyTable.Build(Random.Shared);
        time = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _issued[accountId] = new IssuedTables(time, tables);
        return tables;
    }

    /// <summary>
    /// The password behind clicked positions sent by an account, or null when that account has no tables
    /// outstanding or the positions do not belong to the table they name.
    /// </summary>
    public string Decode(uint accountId, byte tableIndex, string clickedPositions)
    {
        if (!_issued.TryGetValue(accountId, out var issued))
            return null;
        if (tableIndex >= issued.Tables.Length)
            return null;

        return SecondPasswordKeyTable.Decode(issued.Tables[tableIndex], clickedPositions);
    }

    /// <summary>Whether the account has a second password at all.</summary>
    public bool HasPassword(uint accountId) => _passwords.ContainsKey(accountId);

    /// <summary>
    /// Sets the account's first second password. False when one is already set, or the password is empty —
    /// an empty password would be satisfied by an empty click string.
    /// </summary>
    public bool TryCreate(uint accountId, string password)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        var salt = SecondPasswordSecret.NewSalt();
        var stored = new StoredPassword { Salt = salt, Hash = SecondPasswordSecret.Hash(password, salt) };
        return _passwords.TryAdd(accountId, stored);
    }

    /// <summary>
    /// Replaces the password after checking the old one. Returns false when there is no password to change
    /// or the old one is wrong, in which case <paramref name="failedCount"/> counts the wrong answers.
    /// </summary>
    public bool TryChange(uint accountId, string oldPassword, string newPassword, out int failedCount)
    {
        failedCount = 0;
        if (string.IsNullOrEmpty(newPassword) || !_passwords.TryGetValue(accountId, out var stored))
            return false;

        if (!SecondPasswordSecret.Verify(oldPassword, stored.Salt, stored.Hash))
        {
            failedCount = ++stored.FailedCount;
            return false;
        }

        stored.Hash = SecondPasswordSecret.Hash(newPassword, stored.Salt);
        stored.FailedCount = 0;
        return true;
    }

    /// <summary>
    /// Whether a password matches, counting the wrong answers. <paramref name="failedCount"/> is the number
    /// of wrong answers including this one.
    /// </summary>
    public bool Verify(uint accountId, string password, out int failedCount)
    {
        failedCount = 0;
        if (!_passwords.TryGetValue(accountId, out var stored))
            return false;

        if (!SecondPasswordSecret.Verify(password, stored.Salt, stored.Hash))
        {
            failedCount = ++stored.FailedCount;
            return false;
        }

        stored.FailedCount = 0;
        return true;
    }

    /// <summary>Clears the password once it has been given correctly. False when it is wrong or unset.</summary>
    public bool TryClear(uint accountId, string password, out int failedCount)
    {
        if (!Verify(accountId, password, out failedCount))
            return false;

        _passwords.TryRemove(accountId, out _);
        failedCount = 0;
        return true;
    }

    /// <summary>Forgets an account's tables, e.g. once its password has been set or cleared.</summary>
    public void Forget(uint accountId)
    {
        _issued.TryRemove(accountId, out _);
    }
}
