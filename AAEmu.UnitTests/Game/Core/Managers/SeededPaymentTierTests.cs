using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Covers the tier a seeded or newly created account is given.
/// </summary>
/// <remarks>
/// Before the <c>account_payments</c> table existed every account carried a paid subscription, and the
/// entitlement sites branch on <c>AccountPayment.PremiumState</c>. Seeding the free tier instead would
/// move every server's credits tick and labor cap on upgrade, so the default has to preserve the
/// behaviour that predates the table and let an operator opt down deliberately.
/// </remarks>
public class SeededPaymentTierTests
{
    [Test]
    public async Task TheDefaultIsThePaidTier()
    {
        // Read from a fresh config object: the singleton is not seeded in a unit test, and the
        // default has to hold on its own regardless of what any instance has been set to.
        var fresh = new AccountConfig();
        await Assert.That(fresh.SeededPaymentMethod).IsEqualTo(nameof(PaymentMethodType.Premium));
        await Assert.That(AccountManager.ParseSeededPaymentMethod(fresh.SeededPaymentMethod))
            .IsEqualTo(PaymentMethodType.Premium);
    }

    [Test]
    public async Task AnOperatorCanOptDownToTheFreeTier()
    {
        await Assert.That(AccountManager.ParseSeededPaymentMethod(nameof(PaymentMethodType.None)))
            .IsEqualTo(PaymentMethodType.None);
    }

    [Test]
    public async Task TheTierNameIsCaseInsensitive()
    {
        await Assert.That(AccountManager.ParseSeededPaymentMethod("premium"))
            .IsEqualTo(PaymentMethodType.Premium);
    }

    /// <summary>An unrecognised tier is a configuration mistake and must not quietly grant a tier.</summary>
    [Test]
    public async Task AnUnknownTierFailsLoudly()
    {
        await Assert.That(() => AccountManager.ParseSeededPaymentMethod("GoldForever"))
            .Throws<InvalidOperationException>();
    }

    /// <summary>
    /// The row a fresh account INSERT writes, and the row the account_payments migration writes for
    /// every account that had none. Both are the same placeholder, copied out of the two statements
    /// rather than described, so a change to either one shows up here instead of quietly making this
    /// test assert something the server never writes.
    /// </summary>
    private static AccountPaymentRecord PlaceholderRow(uint accountId) => new()
    {
        AccountId = accountId,
        Method = PaymentMethodType.None,
        Location = 0,
        StartTime = AccountPayment.NoSubscriptionTime,
        EndTime = AccountPayment.NoSubscriptionTime,
        BuyPremiumCount = 0,
    };

    /// <summary>A manager over one fixed row, with the seeded tier supplied instead of the singleton.</summary>
    private sealed class StubbedManager(AccountPaymentRecord row) : AccountPaymentManager(() => null)
    {
        public List<AccountPaymentRecord> Saved { get; } = [];

        protected override bool TryReadRecord(uint accountId, out AccountPaymentRecord record)
        {
            record = row;
            return true;
        }

        protected override (PaymentMethodType Method, int Days) ConfiguredSeed() =>
            (PaymentMethodType.Premium, 0);

        public override bool TrySave(uint accountId, AccountPaymentRecord record)
        {
            Saved.Add(record);
            return true;
        }
    }

    /// <summary>
    /// The defect, stated as the row the server actually writes. Before the fix the new-account row
    /// said <c>method=Premium</c> with an epoch window, so <c>PremiumState</c> was false: the label was
    /// set and the entitlement was already over. Credits ticked 100 to 0 on the shipped config.
    /// </summary>
    [Test]
    public async Task TheRowANewAccountGetsIsPremiumWithAWindowThatContainsNow()
    {
        var manager = new StubbedManager(PlaceholderRow(7));
        var payment = new AccountPayment();

        var result = manager.Load(7, payment);

        await Assert.That(result).IsEqualTo(AccountPaymentLoadResult.Loaded);
        await Assert.That(payment.PremiumState).IsTrue();
        await Assert.That(payment.Method).IsEqualTo(PaymentMethodType.Premium);
        await Assert.That(payment.StartTime <= DateTime.UtcNow).IsTrue();
        await Assert.That(payment.EndTime > DateTime.UtcNow).IsTrue();
    }

    /// <summary>
    /// The other half of the same defect: the migration seeds the identical row for every existing
    /// account, so without the upgrade the whole server's accounts read as free. Swept over several
    /// account ids so a fix that seeds one account cannot pass.
    /// </summary>
    [Test]
    public async Task TheRowTheMigrationGetsIsPremiumForEveryExistingAccount()
    {
        foreach (var accountId in new uint[] { 1, 2, 11, 4096 })
        {
            var manager = new StubbedManager(PlaceholderRow(accountId));
            var payment = new AccountPayment();

            manager.Load(accountId, payment);

            await Assert.That(payment.PremiumState).IsTrue();
        }
    }

    /// <summary>The upgrade is persisted, so the second login reads the seeded tier rather than re-seeding.</summary>
    [Test]
    public async Task TheSeededTierIsWrittenBackSoTheNextLoginReadsIt()
    {
        var manager = new StubbedManager(PlaceholderRow(7));
        manager.Load(7, new AccountPayment());

        await Assert.That(manager.Saved.Count).IsEqualTo(1);
        await Assert.That(manager.Saved[0].Method).IsEqualTo(PaymentMethodType.Premium);
        await Assert.That(manager.Saved[0].EndTime).IsGreaterThan(DateTime.UtcNow);
    }

    /// <summary>
    /// An expired subscription is a real state a real purchase leaves behind, and it must not be
    /// quietly re-granted. This is why every field of the placeholder is checked rather than only the
    /// method: a row that merely has a closed window is a lapsed subscription, not a placeholder.
    /// </summary>
    [Test]
    public async Task AnExpiredSubscriptionIsNotSilentlyReGranted()
    {
        var lapsed = PlaceholderRow(7) with
        {
            Method = PaymentMethodType.Premium,
            BuyPremiumCount = 1,
            StartTime = DateTime.UtcNow.AddDays(-30),
            EndTime = DateTime.UtcNow.AddDays(-1),
        };

        var manager = new StubbedManager(lapsed);
        var payment = new AccountPayment();

        manager.Load(7, payment);

        await Assert.That(payment.PremiumState).IsFalse();
        await Assert.That(manager.Saved).IsEmpty();
    }

    /// <summary>A free subscription a player bought and let lapse is likewise left alone.</summary>
    [Test]
    public async Task ALapsedFreeSubscriptionIsNotSilentlyReGranted()
    {
        var lapsed = PlaceholderRow(7) with
        {
            BuyPremiumCount = 1,
            StartTime = DateTime.UtcNow.AddDays(-10),
            EndTime = DateTime.UtcNow.AddDays(-2),
        };

        var manager = new StubbedManager(lapsed);
        var payment = new AccountPayment();

        manager.Load(7, payment);

        await Assert.That(payment.PremiumState).IsFalse();
        await Assert.That(manager.Saved).IsEmpty();
    }

    /// <summary>
    /// A non-positive day count means the window does not close, and it must be a window that is still
    /// <i>open</i> now. This is the second half of the original defect: a paid row whose window had
    /// already closed read as free while the row said Premium, and the far end of the DATETIME range is
    /// deliberately not the epoch that produced it.
    /// </summary>
    [Test]
    public async Task ANonPositiveWindowMeansAnOpenOneAndNotAClosedOne()
    {
        foreach (var days in new[] { 0, -1 })
        {
            var grant = AccountManager.SeededPaymentGrant(7, PaymentMethodType.Premium, days, DateTime.UtcNow);
            var payment = new AccountPayment();
            payment.Apply(grant);

            await Assert.That(grant.EndTime).IsEqualTo(AccountPayment.NoExpiryTime);
            await Assert.That(grant.EndTime).IsGreaterThan(DateTime.UtcNow);
            await Assert.That(payment.PremiumState).IsTrue();
        }
    }

    /// <summary>
    /// The two sentinels are different states and must not be confused. NoSubscriptionTime is a window
    /// that closed at the epoch, which reads as expired; NoExpiryTime is a window that never closes.
    /// </summary>
    [Test]
    public async Task TheTwoWindowSentinelsAreNotTheSameValue()
    {
        await Assert.That(AccountPayment.NoExpiryTime)
            .IsNotEqualTo(AccountPayment.NoSubscriptionTime);
        await Assert.That(AccountPayment.NoExpiryTime).IsGreaterThan(AccountPayment.NoSubscriptionTime);
    }

    /// <summary>
    /// A paid row whose window is the closed sentinel is exactly the shipped defect, and it must report
    /// free. If this ever passes, the open-window change has broken the distinction that matters.
    /// </summary>
    [Test]
    public async Task AWindowClosedAtTheEpochStillReadsAsFree()
    {
        var payment = new AccountPayment();
        payment.Apply(new AccountPaymentRecord
        {
            Method = PaymentMethodType.Premium,
            StartTime = AccountPayment.NoSubscriptionTime,
            EndTime = AccountPayment.NoSubscriptionTime,
        });

        await Assert.That(payment.PremiumState).IsFalse();
    }

    /// <summary>The opt-down path still works: a free seeded tier writes no window at all.</summary>
    [Test]
    public async Task AFreeSeededTierIsWrittenWithNoWindowAndStaysFree()
    {
        var grant = AccountManager.SeededPaymentGrant(7, PaymentMethodType.None, 30, DateTime.UtcNow);

        await Assert.That(grant.Method).IsEqualTo(PaymentMethodType.None);
        await Assert.That(grant.StartTime).IsEqualTo(AccountPayment.NoSubscriptionTime);
        await Assert.That(grant.EndTime).IsEqualTo(AccountPayment.NoSubscriptionTime);

        var payment = new AccountPayment();
        payment.Apply(grant);
        await Assert.That(payment.PremiumState).IsFalse();
    }

    /// <summary>The window is exactly the configured length, counted from the grant.</summary>
    [Test]
    public async Task TheSeededWindowIsTheConfiguredNumberOfDays()
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

        var grant = AccountManager.SeededPaymentGrant(7, PaymentMethodType.Premium, 30, now);

        await Assert.That(grant.StartTime).IsEqualTo(now);
        await Assert.That(grant.EndTime).IsEqualTo(now.AddDays(30));
    }

    /// <summary>
    /// The default window never closes, because nothing renews it. A finite default was a month, and on
    /// that configuration every account went back to free a month later with no purchase to explain it.
    /// </summary>
    [Test]
    public async Task TheDefaultWindowDoesNotExpire()
    {
        var fresh = new AccountConfig();

        await Assert.That(fresh.SeededPaymentDays).IsEqualTo(0);

        var grant = AccountManager.SeededPaymentGrant(
            7, AccountManager.ParseSeededPaymentMethod(fresh.SeededPaymentMethod),
            fresh.SeededPaymentDays, DateTime.UtcNow);

        await Assert.That(grant.EndTime).IsEqualTo(AccountPayment.NoExpiryTime);
    }

    /// <summary>An operator who wants a finite window can still set one, and it is honoured exactly.</summary>
    [Test]
    public async Task AConfiguredFiniteWindowIsStillHonoured()
    {
        // The clock is read once: the grant is computed from this instant, so asking for "now" again
        // afterwards would compare it against a slightly later reading.
        var now = DateTime.UtcNow;
        var grant = AccountManager.SeededPaymentGrant(7, PaymentMethodType.Premium, 45, now);

        await Assert.That(grant.EndTime).IsEqualTo(now.AddDays(45));
        await Assert.That(grant.EndTime).IsNotEqualTo(AccountPayment.NoExpiryTime);
    }

    /// <summary>
    /// The seeded configuration is resolved once at startup, so a bad value stops the server booting
    /// rather than failing one login at a time on a server an operator believes is healthy.
    /// </summary>
    [Test]
    public async Task SeededPaymentConfigurationIsCheckedAtStartup()
    {
        // The configuration singleton is not seeded in a unit test, so this exercises the resolver the
        // startup check calls rather than the singleton itself.
        var method = AccountManager.ParseSeededPaymentMethod(new AccountConfig().SeededPaymentMethod);
        var grant = AccountManager.SeededPaymentGrant(
            1, method, new AccountConfig().SeededPaymentDays, DateTime.UtcNow);

        await Assert.That(grant.Method).IsEqualTo(PaymentMethodType.Premium);
        await Assert.That(grant.EndTime).IsEqualTo(AccountPayment.NoExpiryTime);
    }
}
