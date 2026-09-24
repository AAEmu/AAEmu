using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;

namespace AAEmu.UnitTests.Game.Models.Game;

/// <summary>
/// Acceptance for the account-return rules: every day/count comes from seeded content rows, and a
/// missing row fails loudly instead of falling back.
/// </summary>
[NotInParallel]
public class ReturnAccountRulesTests
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

    private static readonly DateTime Now = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    private void Seed(long restDay, long rewardItemType, long blockDay)
    {
        _data.SetForTest(ReturnAccountRules.RestDayKey, restDay);
        _data.SetForTest(ReturnAccountRules.RewardItemTypeKey, rewardItemType);
        _data.SetForTest(ReturnAccountRules.RewardBlockDayKey, blockDay);
    }

    [Test]
    public async Task RestDay_FromContent_GatesEligibilityByWholeDays()
    {
        Seed(restDay: 3, rewardItemType: 10, blockDay: 0);

        await Assert.That(ReturnAccountRules.IsEligible(Now.AddDays(-2), Now)).IsFalse();
        await Assert.That(ReturnAccountRules.IsEligible(Now.AddDays(-3), Now)).IsTrue();
        await Assert.That(ReturnAccountRules.IsEligible(Now.AddDays(-4), Now)).IsTrue();
        await Assert.That(ReturnAccountRules.IsEligible(Now, Now)).IsFalse();
    }

    [Test]
    public async Task BlockDay_FromContent_BlocksOnceTheAbsencePassesIt()
    {
        Seed(restDay: 1, rewardItemType: 10, blockDay: 5);

        await Assert.That(ReturnAccountRules.IsEligible(Now.AddDays(-5), Now)).IsTrue();
        await Assert.That(ReturnAccountRules.IsEligible(Now.AddDays(-6), Now)).IsFalse();
        await Assert.That(ReturnAccountRules.RewardBlocked(Now.AddDays(-6), Now)).IsTrue();
    }

    [Test]
    public async Task BlockDay_ZeroSentinel_NeverBlocks()
    {
        Seed(restDay: 0, rewardItemType: 10, blockDay: 0);

        await Assert.That(ReturnAccountRules.RewardBlocked(Now.AddDays(-400), Now)).IsFalse();
        await Assert.That(ReturnAccountRules.IsEligible(Now.AddDays(-400), Now)).IsTrue();
    }

    [Test]
    public async Task RewardItemTypeZero_ShipsNoReward_AndIsNeverEligible()
    {
        Seed(restDay: 0, rewardItemType: 0, blockDay: 0);

        await Assert.That(ReturnAccountRules.HasReward).IsFalse();
        await Assert.That(ReturnAccountRules.IsEligible(Now.AddDays(-9), Now)).IsFalse();
    }

    [Test]
    public async Task MissingContentRow_FailsLoudly()
    {
        var threw = false;
        try
        {
            _ = ReturnAccountRules.RestDays;
            _ = ReturnAccountRules.RewardItemType;
            _ = ReturnAccountRules.RewardBlockDays;
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task MissingOneOfThreeRows_FailsLoudly()
    {
        // Rest and reward present, block row missing: the block read still throws.
        _data.SetForTest(ReturnAccountRules.RestDayKey, 1);
        _data.SetForTest(ReturnAccountRules.RewardItemTypeKey, 10);

        var threw = false;
        try
        {
            _ = ReturnAccountRules.RewardBlockDays;
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task DaysSinceSighting_UsesUtcCalendarDays()
    {
        Seed(restDay: 0, rewardItemType: 10, blockDay: 0);
        var lastSeen = new DateTime(2026, 9, 20, 23, 30, 0, DateTimeKind.Utc);

        await Assert.That(ReturnAccountRules.DaysSinceSighting(lastSeen, Now)).IsEqualTo(3);
    }
}
