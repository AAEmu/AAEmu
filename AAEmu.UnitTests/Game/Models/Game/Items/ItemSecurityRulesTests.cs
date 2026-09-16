using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

/// <summary>
/// One of these tests seeds the shared content-config store with a different unlock delay, so the class
/// runs alone: a parallel test that reads the delay would otherwise see the seeded value.
/// </summary>
[NotInParallel]
public class ItemSecurityRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Apply_LocksAnUnlockedItem()
    {
        var item = new Item();

        var change = ItemSecurityRules.Apply(item, true, Now, false);

        await Assert.That(change).IsEqualTo(ItemSecurityChange.Locked);
        await Assert.That(item.HasFlag(ItemFlag.Secure)).IsTrue();
        await Assert.That(item.UnsecureTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task Apply_RefusesATemplateTheClientWillNotLock()
    {
        var item = new Item();

        var change = ItemSecurityRules.Apply(item, true, Now, true);

        await Assert.That(change).IsEqualTo(ItemSecurityChange.Refused);
        await Assert.That(item.HasFlag(ItemFlag.Secure)).IsFalse();
        await Assert.That(item.UnsecureTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task Apply_LeavesAnAlreadyLockedItemAlone()
    {
        var item = new Item { ItemFlags = ItemFlag.Secure };

        var change = ItemSecurityRules.Apply(item, true, Now, false);

        await Assert.That(change).IsEqualTo(ItemSecurityChange.Unchanged);
        await Assert.That(item.UnsecureTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task Apply_StartsTheUnlockDelayAndKeepsTheItemLockedMeanwhile()
    {
        var item = new Item { ItemFlags = ItemFlag.Secure };

        var change = ItemSecurityRules.Apply(item, false, Now, false);

        await Assert.That(change).IsEqualTo(ItemSecurityChange.Unlocking);
        // The flag stays on: this is what the client reads as "unlocking" rather than "unlocked".
        await Assert.That(item.HasFlag(ItemFlag.Secure)).IsTrue();
        await Assert.That(item.UnsecureTime).IsEqualTo(Now.AddMinutes(ItemSecurityRules.UnlockDelayMinutes));
    }

    [Test]
    public async Task Apply_IgnoresUnlockingAnItemThatIsNotLocked()
    {
        var item = new Item();

        var change = ItemSecurityRules.Apply(item, false, Now, false);

        await Assert.That(change).IsEqualTo(ItemSecurityChange.Unchanged);
        await Assert.That(item.UnsecureTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task ExpireUnlock_DropsTheLockOnceTheDelayHasRunOut()
    {
        var item = new Item
        {
            ItemFlags = ItemFlag.Secure,
            UnsecureTime = Now.AddMinutes(-1)
        };

        await Assert.That(ItemSecurityRules.ExpireUnlock(item, Now)).IsTrue();
        await Assert.That(item.HasFlag(ItemFlag.Secure)).IsFalse();
        await Assert.That(item.UnsecureTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task ExpireUnlock_KeepsAPendingUnlock()
    {
        var item = new Item
        {
            ItemFlags = ItemFlag.Secure,
            UnsecureTime = Now.AddMinutes(1)
        };

        await Assert.That(ItemSecurityRules.ExpireUnlock(item, Now)).IsFalse();
        await Assert.That(item.HasFlag(ItemFlag.Secure)).IsTrue();
    }

    [Test]
    public async Task ExpireUnlock_DoesNothingWithoutAPendingDelay()
    {
        // Locked, but no delay was ever set — there is nothing to count down.
        var locked = new Item { ItemFlags = ItemFlag.Secure };
        await Assert.That(ItemSecurityRules.ExpireUnlock(locked, Now)).IsFalse();
        await Assert.That(locked.HasFlag(ItemFlag.Secure)).IsTrue();

        // A leftover timestamp on an already unlocked item is not ours to clear either.
        var plain = new Item { UnsecureTime = Now.AddMinutes(-1) };
        await Assert.That(ItemSecurityRules.ExpireUnlock(plain, Now)).IsFalse();
        await Assert.That(plain.UnsecureTime).IsEqualTo(Now.AddMinutes(-1));
    }

    [Test]
    public async Task Apply_LockingAnUnlockingItemCancelsThePendingUnlock()
    {
        // The state that used to be a dead end: Secure plus a countdown. Locking again has to end
        // the countdown, or the item can neither be locked nor unlocked.
        var item = new Item
        {
            ItemFlags = ItemFlag.Secure,
            UnsecureTime = Now.AddMinutes(ItemSecurityRules.UnlockDelayMinutes - 5)
        };

        var change = ItemSecurityRules.Apply(item, true, Now, false);

        await Assert.That(change).IsEqualTo(ItemSecurityChange.Locked);
        await Assert.That(item.HasFlag(ItemFlag.Secure)).IsTrue();
        await Assert.That(item.UnsecureTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task Apply_ARepeatedUnlockDoesNotPushTheDeadlineOut()
    {
        var deadline = Now.AddMinutes(ItemSecurityRules.UnlockDelayMinutes);
        var item = new Item { ItemFlags = ItemFlag.Secure, UnsecureTime = deadline };

        var change = ItemSecurityRules.Apply(item, false, Now.AddHours(1), false);

        await Assert.That(change).IsEqualTo(ItemSecurityChange.Unchanged);
        await Assert.That(item.UnsecureTime).IsEqualTo(deadline);
    }

    [Test]
    public async Task Apply_RelocksAnItemWhoseUnlockDelayExpired()
    {
        // The relog path leaves the flag set with a timestamp in the past until something looks at
        // the item; locking it again has to end up as a plain lock, not as a "refused" or a no-op.
        var item = new Item
        {
            ItemFlags = ItemFlag.Secure,
            UnsecureTime = Now.AddMinutes(-ItemSecurityRules.UnlockDelayMinutes)
        };

        var change = ItemSecurityRules.Apply(item, true, Now, false);

        await Assert.That(change).IsEqualTo(ItemSecurityChange.Locked);
        await Assert.That(item.HasFlag(ItemFlag.Secure)).IsTrue();
        await Assert.That(item.UnsecureTime).IsEqualTo(DateTime.MinValue);
    }

    [Test]
    public async Task UnlockDelay_MatchesTheWindowTheClientAdvertises()
    {
        // With no content seeded the row is absent, so the value the client itself answers stands:
        // X2Item:GetSecurityUnlockDelayTime() returns 4320 and the dialogs print it as 72 hours.
        await Assert.That(ItemSecurityRules.UnlockDelayMinutes).IsEqualTo(4320);
        await Assert.That(ItemSecurityRules.UnlockDelayMinutes / 60).IsEqualTo(72);
    }

    [Test]
    public async Task UnlockDelay_ComesFromTheContentSettingWhenItIsThere()
    {
        // enum_content_configs id 43 is `item_secure_unlock_delay_time`; the shipped value happens to
        // be the fallback, so a different one is seeded here to prove the setting is what is read.
        var config = ContentConfigGameData.Instance;
        config.SetForTest(ItemSecurityRules.UnlockDelayConfigName, 120);

        try
        {
            var item = new Item { ItemFlags = ItemFlag.Secure };
            var change = ItemSecurityRules.Apply(item, false, Now, false);

            await Assert.That(ItemSecurityRules.UnlockDelayMinutes).IsEqualTo(120);
            await Assert.That(change).IsEqualTo(ItemSecurityChange.Unlocking);
            await Assert.That(item.UnsecureTime).IsEqualTo(Now.AddMinutes(120));
        }
        finally
        {
            config.SetForTest(ItemSecurityRules.UnlockDelayConfigName, ItemSecurityRules.DefaultUnlockDelayMinutes);
        }
    }
}
