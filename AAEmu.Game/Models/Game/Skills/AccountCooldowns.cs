using System.Collections.Concurrent;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The <c>skills.account_cooldown</c> store: a cooldown that follows the account rather than the
/// character, so logging another character on the same account does not clear it.
/// </summary>
/// <remarks>
/// All 17 shipped rows are consumable limits rather than combat pacing — the six 노동력 (labor) potions
/// 35180-35187, 마력 증폭기 주입하기 38380-38382, and the 제이크 potions 39367/39368/39530/39531 — which is
/// why the store is account-scoped: the item's whole purpose is to stop one account drinking the same
/// potion on every character.
///
/// Lifetime: the store lives in the process, so it survives a character switch and a relog on the same
/// running server but not a server restart. Persisting it needs a new MySQL table, which is out of scope
/// for this batch; the cooldowns on these rows are in the 10-minute to hour range, so a restart is the
/// only way to clear one early.
/// </remarks>
public static class AccountCooldowns
{
    private static readonly ConcurrentDictionary<uint, ConcurrentDictionary<uint, DateTime>> Store = new();

    /// <summary>Arms (or re-arms) one skill's account cooldown for an account.</summary>
    public static void Arm(uint accountId, uint skillId, uint duration)
    {
        if (accountId == 0 || skillId == 0)
            return;

        var endTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(duration);
        var skills = Store.GetOrAdd(accountId, _ => new ConcurrentDictionary<uint, DateTime>());
        skills[skillId] = endTime;
    }

    /// <summary>True while the account still has this skill on cooldown.</summary>
    public static bool IsActive(uint accountId, uint skillId)
    {
        if (accountId == 0 || skillId == 0 || !Store.TryGetValue(accountId, out var skills))
            return false;

        if (!skills.TryGetValue(skillId, out var endTime))
            return false;

        if (endTime > DateTime.UtcNow)
            return true;

        skills.TryRemove(skillId, out _);
        return false;
    }

    /// <summary>Remaining account cooldown for one skill, or <see cref="TimeSpan.Zero"/>.</summary>
    public static TimeSpan GetRemaining(uint accountId, uint skillId)
    {
        if (accountId == 0 || skillId == 0 || !Store.TryGetValue(accountId, out var skills) ||
            !skills.TryGetValue(skillId, out var endTime))
            return TimeSpan.Zero;

        return endTime > DateTime.UtcNow ? endTime - DateTime.UtcNow : TimeSpan.Zero;
    }

    /// <summary>Drops every cooldown held for an account (used by tests and account teardown).</summary>
    public static void Clear(uint accountId)
        => Store.TryRemove(accountId, out _);
}
