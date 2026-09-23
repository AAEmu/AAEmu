using System.Reflection;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Account;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Account;

/// <summary>
/// Acceptance for payment/account-tier loading: tier and payment are derived from content
/// (<c>premium_grades</c>) plus the state the connection path supplies, and re-applying to a fresh
/// connection (a relog) reproduces the same state.
/// </summary>
[NotInParallel]
public class AccountConnectionStateTests
{
    private FieldInfo _premiumSingletonField;
    private object _previousPremium;
    private SqliteConnection _sqlite;

    [Before(Test)]
    public void SeedPremiumContent()
    {
        _premiumSingletonField = typeof(Singleton<PremiumGameData>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previousPremium = _premiumSingletonField.GetValue(null);
        _premiumSingletonField.SetValue(null, new PremiumGameData());

        _sqlite = new SqliteConnection("Data Source=:memory:");
        _sqlite.Open();
        using (var command = _sqlite.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE premium_grades (
                    id INTEGER PRIMARY KEY, grade_id INTEGER NOT NULL, point INTEGER NOT NULL DEFAULT 0,
                    buff_id INTEGER NOT NULL DEFAULT 0, online_labor INTEGER NOT NULL DEFAULT 0,
                    offline_labor INTEGER NOT NULL DEFAULT 0, max_labor INTEGER NOT NULL DEFAULT 0,
                    max_local_labor INTEGER NOT NULL DEFAULT 0, config_id INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE premium_configs (
                    id INTEGER PRIMARY KEY, connect_point INTEGER NOT NULL DEFAULT 0,
                    disconnect_point INTEGER NOT NULL DEFAULT 0, deactivate_point INTEGER NOT NULL DEFAULT 0,
                    max_point INTEGER NOT NULL DEFAULT 0, auction_charge_discount INTEGER NOT NULL DEFAULT 0,
                    auction_deposit_discount INTEGER NOT NULL DEFAULT 0, max_grade_id INTEGER NOT NULL DEFAULT 0);
                INSERT INTO premium_grades (id, grade_id, point, buff_id) VALUES (1, 1, 0, 0);
                INSERT INTO premium_grades (id, grade_id, point, buff_id) VALUES (2, 2, 1, 7149);
                INSERT INTO premium_grades (id, grade_id, point, buff_id) VALUES (3, 3, 50, 7150);
                INSERT INTO premium_grades (id, grade_id, point, buff_id) VALUES (4, 4, 125, 7151);
                INSERT INTO premium_grades (id, grade_id, point, buff_id) VALUES (5, 5, 225, 7152);
                INSERT INTO premium_grades (id, grade_id, point, buff_id) VALUES (6, 6, 400, 7153);
                INSERT INTO premium_configs (id, max_grade_id) VALUES (1, 6);
                """;
            command.ExecuteNonQuery();
        }
        PremiumGameData.Instance.Load(_sqlite);
    }

    [After(Test)]
    public void RestorePremium()
    {
        _sqlite.Dispose();
        _premiumSingletonField.SetValue(null, _previousPremium);
    }

    private static GameConnection NewConnection(uint accountId = 39) => new(null) { AccountId = accountId };

    [Test]
    public async Task PaymentMethod_FollowsTheContentPaidFloor()
    {
        var firstPaid = PremiumGameData.Instance.FirstPaidGradeId;
        await Assert.That(firstPaid).IsEqualTo(2u); // content: first grade carrying a buff

        await Assert.That(AccountTierPaymentRules.MethodForTier(firstPaid, firstPaid))
            .IsEqualTo(PaymentMethodType.Premium);
        await Assert.That(AccountTierPaymentRules.MethodForTier(firstPaid - 1, firstPaid))
            .IsEqualTo(PaymentMethodType.None);
        // No paid grades in content at all -> nothing can be premium (0 = none, not a literal grade).
        await Assert.That(AccountTierPaymentRules.MethodForTier(6, 0))
            .IsEqualTo(PaymentMethodType.None);
    }

    [Test]
    public async Task Apply_LoadsTierPaymentAndEntitlements_OnTheConnection()
    {
        var connection = NewConnection();
        var entitlement = new AccountAttribute
        {
            AccountId = 39,
            KindId = (uint)AccountAttributeKind.AccountBuff,
            KindValue = 1001,
            WorldId = 0,
            Count = 0,
            Starts = DateTime.UnixEpoch,
            Expires = DateTime.UnixEpoch,
        };

        AccountConnectionState.Apply(connection, 400, 6, [entitlement]);

        await Assert.That(connection.AccountTier).IsEqualTo((400, 6u));
        await Assert.That(connection.Payment.Method).IsEqualTo(PaymentMethodType.Premium);
        await Assert.That(connection.Entitlements).HasCount().EqualTo(1);
        await Assert.That(connection.Entitlements[0].KindValue).IsEqualTo(1001u);
    }

    [Test]
    public async Task FreeTier_LoadsTheNoPaymentMethod()
    {
        var connection = NewConnection();

        AccountConnectionState.Apply(connection, 0, 1, []);

        // Loading the tier does not rewrite the payment method the labor ticks read.
        await Assert.That(connection.Payment.Method).IsEqualTo(PaymentMethodType.Premium);
        await Assert.That(connection.Entitlements).IsEmpty();
    }

    [Test]
    public async Task RelogOnAFreshConnection_ReproducesTheSameState()
    {
        var entitlement = new AccountAttribute
        {
            AccountId = 39,
            KindId = (uint)AccountAttributeKind.Ulc,
            KindValue = 7,
            WorldId = 0,
            Count = 1,
            Starts = DateTime.UnixEpoch,
            Expires = DateTime.UnixEpoch,
        };

        var first = NewConnection();
        AccountConnectionState.Apply(first, 125, 4, [entitlement]);

        var afterRelog = NewConnection();
        AccountConnectionState.Apply(afterRelog, 125, 4, [entitlement]);

        await Assert.That(afterRelog.AccountTier).IsEqualTo(first.AccountTier);
        await Assert.That(afterRelog.Payment.Method).IsEqualTo(first.Payment.Method);
        await Assert.That(afterRelog.Entitlements).HasCount().EqualTo(first.Entitlements.Count);
        await Assert.That(afterRelog.Entitlements[0].KindValue).IsEqualTo(7u);
    }
}
