using System.Reflection;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The claim gate before any database work: content decides whether a claim is even attempted, and a
/// missing last-seen timestamp refuses instead of granting. Exactly-once itself is the
/// <c>account_return_claims</c> primary key and is exercised against MySQL in the integration tests.
/// </summary>
[NotInParallel]
public class AccountReturnManagerTests
{
    private FieldInfo _contentConfigSingletonField;
    private object _previousContentConfig;
    private ContentConfigGameData _data;

    [Before(Test)]
    public void SetupContentConfig()
    {
        _contentConfigSingletonField = typeof(Singleton<ContentConfigGameData>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previousContentConfig = _contentConfigSingletonField.GetValue(null);
        _data = new ContentConfigGameData();
        _contentConfigSingletonField.SetValue(null, _data);
    }

    [After(Test)]
    public void RestoreContentConfig()
    {
        _contentConfigSingletonField.SetValue(null, _previousContentConfig);
    }

    private static AccountReturnManager ManagerThatRefusesConnections(out Func<int> factoryCalls)
    {
        var calls = 0;
        factoryCalls = () => calls;
        return new AccountReturnManager(() =>
        {
            calls++;
            throw new InvalidOperationException("no database in this test");
        });
    }

    [Test]
    public async Task Claim_WithTheShippedZeroRewardRow_SkipsLoudly_AndNeverTouchesTheDatabase()
    {
        _data.SetForTest(ReturnAccountRules.RestDayKey, 0);
        _data.SetForTest(ReturnAccountRules.RewardItemTypeKey, ReturnAccountRules.NoRewardItemType);
        _data.SetForTest(ReturnAccountRules.RewardBlockDayKey, 0);
        var manager = ManagerThatRefusesConnections(out var factoryCalls);

        var result = manager.TryClaim(39, (_, _) => true);

        await Assert.That(result).IsEqualTo(AccountReturnClaimResult.NoRewardConfigured);
        await Assert.That(factoryCalls()).IsEqualTo(0);
    }

    [Test]
    public async Task Claim_WithNoContentRowsAtAll_FailsLoudly_AndNeverTouchesTheDatabase()
    {
        var manager = ManagerThatRefusesConnections(out var factoryCalls);

        var threw = false;
        try
        {
            manager.TryClaim(39, (_, _) => true);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
        await Assert.That(factoryCalls()).IsEqualTo(0);
    }

    [Test]
    public async Task Availability_WithTheShippedZeroRewardRow_IsFalse_WithoutTouchingTheDatabase()
    {
        _data.SetForTest(ReturnAccountRules.RestDayKey, 0);
        _data.SetForTest(ReturnAccountRules.RewardItemTypeKey, ReturnAccountRules.NoRewardItemType);
        _data.SetForTest(ReturnAccountRules.RewardBlockDayKey, 0);
        var manager = ManagerThatRefusesConnections(out var factoryCalls);

        await Assert.That(manager.IsRewardAvailable(39)).IsFalse();
        await Assert.That(factoryCalls()).IsEqualTo(0);
    }

    [Test]
    public async Task Claim_WithAnUnreadableLastSeen_IsRefusedAsNotEligible()
    {
        _data.SetForTest(ReturnAccountRules.RestDayKey, 0);
        _data.SetForTest(ReturnAccountRules.RewardItemTypeKey, 2);
        _data.SetForTest(ReturnAccountRules.RewardBlockDayKey, 0);
        var manager = ManagerThatRefusesConnections(out _);

        var grants = 0;
        var result = manager.TryClaim(39, (_, _) => { grants++; return true; });

        await Assert.That(result).IsEqualTo(AccountReturnClaimResult.NotEligible);
        await Assert.That(grants).IsEqualTo(0);
    }

    [Test]
    public async Task StatusPacket_WritesTheAvailabilityByte()
    {
        var taken = new SCReturnAccountStatusPacket(false).Write(new PacketStream()).GetBytes();
        var available = new SCReturnAccountStatusPacket(true).Write(new PacketStream()).GetBytes();

        await Assert.That(taken).IsEquivalentTo(new byte[] { 0 });
        await Assert.That(available).IsEquivalentTo(new byte[] { 1 });
    }
}
