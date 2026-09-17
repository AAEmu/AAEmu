using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.SecondPassword;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// The second password: the key tables a client reads its window from, and the password that comes back as
/// clicked positions. Both are per account, which is what the window itself is — the actions it guards are
/// account-wide (trading, destroying) rather than tied to the character that happens to be online.
/// </summary>
/// <remarks>
/// The stored secret lives in <see cref="ISecondPasswordStore"/>, so it outlives a World process: the
/// actions the password guards are account-wide, and a guard that forgets is a guard that fails open.
/// </remarks>
public class SecondPasswordManager(ISecondPasswordStore store) : Singleton<SecondPasswordManager>
{
    /// <summary>
    /// How many attempts an account may make inside one <see cref="AttemptWindow"/> before it has to wait.
    /// </summary>
    /// <remarks>
    /// A throttle rather than a lock-out: the retail limit and lock duration are values we have not read,
    /// and guessing them would lock players out on a guess, but "no limit at all" is not the only
    /// alternative to knowing them. It also bounds what a wrong password can cost the server, since each
    /// attempt is a key derivation and a rejected attempt is not.
    /// </remarks>
    public const int AttemptsPerWindow = 5;

    /// <summary>The window those attempts are counted in.</summary>
    public static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(1);

    private sealed record IssuedTables(uint Time, string[] Tables);

    private sealed class Attempts
    {
        public DateTime WindowStart { get; set; }
        public int Count { get; set; }
    }

    private readonly ConcurrentDictionary<uint, IssuedTables> _issued = new();
    private readonly ConcurrentDictionary<uint, SecondPasswordRecord> _passwords = new();
    private readonly ConcurrentDictionary<uint, Attempts> _attempts = new();
    private readonly object _attemptLock = new();

    /// <summary>The tables handed out for an account, and the time they were issued with.</summary>
    public string[] Issue(uint accountId, out uint time)
    {
        var tables = SecondPasswordKeyTable.Build();
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

    /// <summary>
    /// Whether the account may make another attempt now. False means it has used its attempts for the
    /// current window; the caller answers without checking the password, so a rejected attempt costs a
    /// key derivation nothing.
    /// </summary>
    public bool TryBeginAttempt(uint accountId, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var now = DateTime.UtcNow;

        lock (_attemptLock)
        {
            if (!_attempts.TryGetValue(accountId, out var attempts) ||
                now - attempts.WindowStart >= AttemptWindow)
            {
                _attempts[accountId] = new Attempts { WindowStart = now, Count = 1 };
                return true;
            }

            if (attempts.Count >= AttemptsPerWindow)
            {
                retryAfter = AttemptWindow - (now - attempts.WindowStart);
                return false;
            }

            attempts.Count++;
            return true;
        }
    }

    /// <summary>Whether the account has a second password at all.</summary>
    public bool HasPassword(uint accountId) => Password(accountId) != null;

    /// <summary>
    /// Sets the account's first second password. False when one is already set, or the password is empty —
    /// an empty password would be satisfied by an empty click string.
    /// </summary>
    public bool TryCreate(uint accountId, string password)
    {
        if (string.IsNullOrEmpty(password))
            return false;
        if (Password(accountId) != null)
            return false;

        var salt = SecondPasswordSecret.NewSalt();
        var record = new SecondPasswordRecord
        {
            AccountId = accountId,
            Salt = salt,
            Hash = SecondPasswordSecret.Hash(password, salt)
        };

        if (!_passwords.TryAdd(accountId, record))
            return false;

        store.Save(record);
        return true;
    }

    /// <summary>
    /// Replaces the password after checking the old one. Returns false when there is no password to change
    /// or the old one is wrong, in which case <paramref name="failedCount"/> counts the wrong answers.
    /// </summary>
    public bool TryChange(uint accountId, string oldPassword, string newPassword, out int failedCount)
    {
        failedCount = 0;
        var stored = Password(accountId);
        if (string.IsNullOrEmpty(newPassword) || stored == null)
            return false;

        if (!SecondPasswordSecret.Verify(oldPassword, stored.Salt, stored.Hash))
        {
            failedCount = stored.FailedCount + 1;
            Save(accountId, stored.Salt, stored.Hash, failedCount);
            return false;
        }

        // A password change is where the salt is rolled: the new secret gets its own, so an old hash is
        // not a shorter path to the new one.
        var salt = SecondPasswordSecret.NewSalt();
        Save(accountId, salt, SecondPasswordSecret.Hash(newPassword, salt), 0);
        return true;
    }

    /// <summary>
    /// Whether a password matches, counting the wrong answers. <paramref name="failedCount"/> is the number
    /// of wrong answers including this one.
    /// </summary>
    public bool Verify(uint accountId, string password, out int failedCount)
    {
        failedCount = 0;
        var stored = Password(accountId);
        if (stored == null)
            return false;

        if (!SecondPasswordSecret.Verify(password, stored.Salt, stored.Hash))
        {
            failedCount = stored.FailedCount + 1;
            Save(accountId, stored.Salt, stored.Hash, failedCount);
            return false;
        }

        if (stored.FailedCount != 0)
            Save(accountId, stored.Salt, stored.Hash, 0);

        return true;
    }

    /// <summary>Clears the password once it has been given correctly. False when it is wrong or unset.</summary>
    public bool TryClear(uint accountId, string password, out int failedCount)
    {
        if (!Verify(accountId, password, out failedCount))
            return false;

        _passwords.TryRemove(accountId, out _);
        store.Delete(accountId);
        failedCount = 0;
        return true;
    }

    /// <summary>Forgets an account's tables, e.g. once its password has been set or cleared.</summary>
    public void Forget(uint accountId)
    {
        _issued.TryRemove(accountId, out _);
    }

    /// <summary>The account's stored password, read from the store the first time it is asked for.</summary>
    private SecondPasswordRecord Password(uint accountId)
    {
        if (_passwords.TryGetValue(accountId, out var cached))
            return cached;

        var loaded = store.Load(accountId);
        if (loaded == null)
            return null;

        _passwords[accountId] = loaded;
        return loaded;
    }

    /// <summary>Writes a change back, keeping the cached record in step with it.</summary>
    private void Save(uint accountId, byte[] salt, string hash, int failedCount)
    {
        var record = new SecondPasswordRecord
        {
            AccountId = accountId,
            Salt = salt,
            Hash = hash,
            FailedCount = failedCount
        };

        _passwords[accountId] = record;
        store.Save(record);
    }
}
