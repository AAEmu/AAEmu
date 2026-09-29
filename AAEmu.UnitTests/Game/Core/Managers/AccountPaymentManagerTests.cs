using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;

using MySql.Data.MySqlClient;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The tier decision table. The regression these pin is that an account's payment used to be a fixed
/// premium subscription asserted in code, so every connection read as paid and every entitlement
/// keyed off it took the paid branch. An unloaded payment must report no entitlement, and a loaded
/// one must report exactly what the row says.
/// </summary>
[NotInParallel]
public class AccountPaymentManagerTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static AccountPaymentRecord Premium(int location = 0, int buyCount = 1) => new()
    {
        AccountId = 7,
        Method = PaymentMethodType.Premium,
        Location = location,
        StartTime = Now.AddDays(-1),
        EndTime = Now.AddDays(30),
        BuyPremiumCount = buyCount,
    };

    /// <summary>A manager whose database answers with a fixed outcome, so the table is testable
    /// without MySQL. The row-reading seam is the one production uses.</summary>
    private sealed class StubbedManager(Func<uint, AccountPaymentRecord> read) : AccountPaymentManager(
        () => throw new InvalidOperationException("the stub answers without a database"))
    {
        protected override bool TryReadRecord(uint accountId, out AccountPaymentRecord record)
        {
            record = read(accountId);
            return record != null;
        }
    }

    private static AccountPaymentManager ManagerThatThrows(out Func<int> factoryCalls)
    {
        var calls = 0;
        factoryCalls = () => calls;
        return new AccountPaymentManager(() =>
        {
            calls++;
            throw new InvalidOperationException("no database in this test");
        });
    }

    private static AccountPayment FreshPayment() => new();

    [Test]
    public async Task ANewPaymentCarriesNoEntitlement_BecauseNothingWasLoaded()
    {
        var payment = FreshPayment();

        await Assert.That(payment.IsLoaded).IsFalse();
        await Assert.That(payment.PremiumState).IsFalse();
        await Assert.That(payment.Method).IsEqualTo(PaymentMethodType.None);
        await Assert.That(payment.BuyPremiumCount).IsEqualTo(0);
    }

    [Test]
    public async Task AnUnreadableRow_RefusesThePaidTier_AndLeavesNoEntitlement()
    {
        var manager = ManagerThatThrows(out var factoryCalls);
        var payment = FreshPayment();
        // A paid row from an earlier read of this same connection.
        payment.Apply(Premium());

        var result = manager.Load(7, payment);

        await Assert.That(result).IsEqualTo(AccountPaymentLoadResult.Unreadable);
        await Assert.That(factoryCalls()).IsEqualTo(1);
        await Assert.That(payment.IsLoaded).IsFalse();
        await Assert.That(payment.PremiumState).IsFalse();
        await Assert.That(payment.Method).IsEqualTo(PaymentMethodType.None);
    }

    [Test]
    public async Task AMissingRow_RefusesThePaidTier()
    {
        var manager = new StubbedManager(_ => null);
        var payment = FreshPayment();

        var result = manager.Load(7, payment);

        await Assert.That(result).IsEqualTo(AccountPaymentLoadResult.NoRecord);
        await Assert.That(payment.IsLoaded).IsFalse();
        await Assert.That(payment.PremiumState).IsFalse();
    }

    [Test]
    public async Task AnUnknownMethodValue_RefusesThePaidTier()
    {
        var manager = new StubbedManager(_ => Premium() with { Method = (PaymentMethodType)99 });
        var payment = FreshPayment();

        var result = manager.Load(7, payment);

        await Assert.That(result).IsEqualTo(AccountPaymentLoadResult.Unreadable);
        await Assert.That(payment.IsLoaded).IsFalse();
        await Assert.That(payment.PremiumState).IsFalse();
    }

    [Test]
    public async Task AnActivePremiumRow_GrantsThePaidTier()
    {
        var manager = new StubbedManager(_ => Premium(location: 3, buyCount: 4));
        var payment = FreshPayment();

        var result = manager.Load(7, payment);

        await Assert.That(result).IsEqualTo(AccountPaymentLoadResult.Loaded);
        await Assert.That(payment.IsLoaded).IsTrue();
        await Assert.That(payment.PremiumState).IsTrue();
        await Assert.That(payment.Method).IsEqualTo(PaymentMethodType.Premium);
        await Assert.That(payment.Location).IsEqualTo(3);
        await Assert.That(payment.BuyPremiumCount).IsEqualTo(4);
    }

    [Test]
    public async Task AnExpiredWindow_IsNotPremium_AndCarriesNoRemainingTime()
    {
        var manager = new StubbedManager(_ => Premium() with { EndTime = Now.AddDays(-1) });
        var payment = FreshPayment();

        var result = manager.Load(7, payment);

        await Assert.That(result).IsEqualTo(AccountPaymentLoadResult.Loaded);
        await Assert.That(payment.PremiumState).IsFalse();
        await Assert.That(payment.RealPayTimeSeconds).IsEqualTo(0L);
    }

    [Test]
    public async Task AWindowThatHasNotOpened_IsNotPremium()
    {
        var manager = new StubbedManager(_ => Premium() with { StartTime = Now.AddDays(1) });
        var payment = FreshPayment();

        var result = manager.Load(7, payment);

        await Assert.That(result).IsEqualTo(AccountPaymentLoadResult.Loaded);
        await Assert.That(payment.PremiumState).IsFalse();
    }

    [Test]
    [Arguments(PaymentMethodType.None)]
    [Arguments(PaymentMethodType.Demo)]
    public async Task ANonPremiumMethod_IsNotPremium(PaymentMethodType method)
    {
        var manager = new StubbedManager(_ => Premium() with { Method = method });
        var payment = FreshPayment();

        var result = manager.Load(7, payment);

        await Assert.That(result).IsEqualTo(AccountPaymentLoadResult.Loaded);
        await Assert.That(payment.IsLoaded).IsTrue();
        await Assert.That(payment.PremiumState).IsFalse();
    }

    [Test]
    public async Task ReloadingUnderTheSameConnection_TakesTheNewRowOverTheOld()
    {
        var loaded = Premium();
        var manager = new StubbedManager(_ => loaded);
        var payment = FreshPayment();

        _ = manager.Load(7, payment);
        await Assert.That(payment.PremiumState).IsTrue();

        // A subscription that lapsed between two connections must not stay granted in the
        // reused payment object.
        loaded = loaded with { EndTime = Now.AddDays(-1) };
        var result = manager.Load(7, payment);

        await Assert.That(result).IsEqualTo(AccountPaymentLoadResult.Loaded);
        await Assert.That(payment.PremiumState).IsFalse();
    }

    [Test]
    public async Task AFailedReloadClearsAPreviouslyGrantedTier()
    {
        var manager = new StubbedManager(_ => Premium());
        var payment = FreshPayment();
        _ = manager.Load(7, payment);
        await Assert.That(payment.PremiumState).IsTrue();

        var result = new StubbedManager(_ => null).Load(7, payment);

        await Assert.That(result).IsEqualTo(AccountPaymentLoadResult.NoRecord);
        await Assert.That(payment.PremiumState).IsFalse();
        await Assert.That(payment.IsLoaded).IsFalse();
    }

    [Test]
    public async Task RemainingTimeCountsDownFromTheLoadedWindow()
    {
        var manager = new StubbedManager(_ => Premium() with { EndTime = Now.AddMinutes(5) });
        var payment = FreshPayment();
        _ = manager.Load(7, payment);

        var remaining = payment.RealPayTimeSeconds;

        await Assert.That(remaining).IsGreaterThan(0L);
        await Assert.That(remaining).IsLessThanOrEqualTo((long)TimeSpan.FromMinutes(5).TotalSeconds);
    }

    [Test]
    public async Task SavingUnderAnotherAccountIsRefused()
    {
        var manager = ManagerThatThrows(out var factoryCalls);

        var threw = false;
        try
        {
            manager.TrySave(9, Premium());
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
        await Assert.That(factoryCalls()).IsEqualTo(0);
    }

    [Test]
    public async Task AccountInfoPacket_PublishesTheLoadedTier()
    {
        var payment = FreshPayment();
        new StubbedManager(_ => Premium(location: 2, buyCount: 5)).Load(7, payment);

        var bytes = new SCAccountInfoPacket(
            (int)payment.Method,
            payment.Location,
            payment.StartTime,
            payment.EndTime,
            payment.RealPayTimeSeconds,
            payment.BuyPremiumCount).Write(new PacketStream()).GetBytes();

        // method (i32) + location (i32) + payStart (i64 unix) + payEnd (i64 unix)
        //   + realPayTime (i64) + buyPremiumCount (i32)
        await Assert.That(bytes.Length).IsEqualTo(4 + 4 + 8 + 8 + 8 + 4);
        await Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 0, 4)).IsEqualTo("\u0001\0\0\0");
        await Assert.That(payment.PremiumState).IsTrue();
    }

    [Test]
    public async Task AccountInfoPacket_PublishesNoEntitlementForAnUnloadedAccount()
    {
        var payment = FreshPayment();
        new StubbedManager(_ => null).Load(7, payment);

        var bytes = new SCAccountInfoPacket(
            (int)payment.Method,
            payment.Location,
            payment.StartTime,
            payment.EndTime,
            payment.RealPayTimeSeconds,
            payment.BuyPremiumCount).Write(new PacketStream()).GetBytes();

        await Assert.That(bytes.Length).IsEqualTo(4 + 4 + 8 + 8 + 8 + 4);
        // method 5 (none), not the premium the old fixed record always sent.
        await Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 0, 4)).IsEqualTo("\u0005\0\0\0");
        await Assert.That(payment.RealPayTimeSeconds).IsEqualTo(0L);
    }
}
