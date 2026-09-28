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
    /// A seeded account on the default tier is premium, so it keeps the entitlement the sites branch
    /// on. This is the regression the review named: the free tier made <c>PremiumState</c> false for
    /// every account on every server.
    /// </summary>
    [Test]
    public async Task TheDefaultSeededTierGrantsPremiumState()
    {
        var payment = new AccountPayment();
        await Assert.That(payment.PremiumState).IsFalse();  // not loaded is never premium

        payment.Apply(new AccountPaymentRecord
        {
            Method = AccountManager.ParseSeededPaymentMethod(new AccountConfig().SeededPaymentMethod),
            Location = 0,
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow.AddHours(1),
        });
        await Assert.That(payment.PremiumState).IsTrue();
    }

    /// <summary>The opt-down path is what a server without entitlements actually runs.</summary>
    [Test]
    public async Task TheFreeTierLeavesPremiumStateFalse()
    {
        var payment = new AccountPayment();
        payment.Apply(new AccountPaymentRecord
        {
            Method = AccountManager.ParseSeededPaymentMethod(nameof(PaymentMethodType.None)),
            Location = 0,
            StartTime = DateTime.UtcNow.AddHours(-1),
            EndTime = DateTime.UtcNow.AddHours(1),
        });
        await Assert.That(payment.PremiumState).IsFalse();
    }
}
