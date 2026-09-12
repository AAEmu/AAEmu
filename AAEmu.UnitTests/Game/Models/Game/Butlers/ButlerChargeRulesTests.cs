using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.UnitTests.Game.Models.Game.Butlers;

[NotInParallel]
public class ButlerChargeRulesTests
{
    private FieldInfo _contentConfigSingletonField;
    private object _previousContentConfig;

    [Before(Test)]
    public void SetupContentConfig()
    {
        _contentConfigSingletonField = typeof(Singleton<ContentConfigGameData>)
            .GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        _previousContentConfig = _contentConfigSingletonField.GetValue(null);
        _contentConfigSingletonField.SetValue(null, new ContentConfigGameData());
    }

    [After(Test)]
    public void RestoreContentConfig()
    {
        _contentConfigSingletonField.SetValue(null, _previousContentConfig);
    }

    [Test]
    public async Task LaborPowerCharge_UsesFullRateWithoutCrossingTheDailyQuota()
    {
        var accepted = ButlerChargeRules.TryQuoteLaborPowerCharge(
            new ButlerLaborPowerChargeRequest(100, 500, 2_000, 900, 1_000, 5, 80), out var quote,
            out var failure);

        await Assert.That(accepted).IsTrue();
        await Assert.That(failure).IsEqualTo(ButlerChargeFailure.None);
        await Assert.That(quote).IsEqualTo(new ButlerLaborPowerChargeQuote(
            ButlerLaborPowerChargePhase.DailyFullRate, 100, 100, 600, 1_000));
    }

    [Test]
    public async Task LaborPowerCharge_RejectsARequestThatWouldCrossIntoReducedRate()
    {
        var accepted = ButlerChargeRules.TryQuoteLaborPowerCharge(
            new ButlerLaborPowerChargeRequest(2, 500, 2_000, 999, 1_000, 5, 80), out _, out var failure);

        await Assert.That(accepted).IsFalse();
        await Assert.That(failure).IsEqualTo(ButlerChargeFailure.DailyLaborPowerQuotaExceeded);
    }

    [Test]
    public async Task LaborPowerCharge_UsesConfiguredReducedRateAndMinimum()
    {
        var accepted = ButlerChargeRules.TryQuoteLaborPowerCharge(
            new ButlerLaborPowerChargeRequest(9, 500, 2_000, 1_000, 1_000, 5, 80), out var quote,
            out var failure);

        await Assert.That(accepted).IsTrue();
        await Assert.That(failure).IsEqualTo(ButlerChargeFailure.None);
        await Assert.That(quote).IsEqualTo(new ButlerLaborPowerChargeQuote(
            ButlerLaborPowerChargePhase.ReducedRate, 9, 7, 507, 1_000));

        var tooSmall = ButlerChargeRules.TryQuoteLaborPowerCharge(
            new ButlerLaborPowerChargeRequest(4, 500, 2_000, 1_000, 1_000, 5, 80), out _, out failure);
        await Assert.That(tooSmall).IsFalse();
        await Assert.That(failure).IsEqualTo(ButlerChargeFailure.InvalidAmount);
    }

    [Test]
    public async Task LaborPowerCharge_ReducedRateKeepsTheExhaustedDailyCounterStable()
    {
        var request = new ButlerLaborPowerChargeRequest(5, 500, 10_000, 1_000, 1_000, 5, 80);

        for (var i = 0; i < 100; i++)
        {
            var accepted = ButlerChargeRules.TryQuoteLaborPowerCharge(request, out var quote, out var failure);
            await Assert.That(accepted).IsTrue();
            await Assert.That(failure).IsEqualTo(ButlerChargeFailure.None);
            await Assert.That(quote.Phase).IsEqualTo(ButlerLaborPowerChargePhase.ReducedRate);
            await Assert.That(quote.NewDailyChargedAmount).IsEqualTo((ushort)1_000);
            request = request with
            {
                CurrentButlerLaborPower = quote.NewButlerLaborPower,
                CurrentDailyChargedAmount = quote.NewDailyChargedAmount
            };
        }
    }

    [Test]
    public async Task FreeProductionCostCharge_CapsGrantAndIncrementsBothWeeklyCounters()
    {
        var request = new ButlerProductionCostChargeRequest(800, 1_000, new ButlerProductionCostChargeCounters(1, 19_800, 77),
            20_000);

        var accepted = ButlerChargeRules.TryQuoteFreeProductionCostCharge(request, 2, 500, out var quote, out var failure);

        await Assert.That(accepted).IsTrue();
        await Assert.That(failure).IsEqualTo(ButlerChargeFailure.None);
        await Assert.That(quote).IsEqualTo(new ButlerProductionCostChargeQuote(200, 1_000,
            new ButlerProductionCostChargeCounters(2, 20_000, 77)));
    }

    [Test]
    public async Task PaidProductionCostCharge_RequiresTheResolvedEffectToFitBothCaps()
    {
        var request = new ButlerProductionCostChargeRequest(700, 1_000, new ButlerProductionCostChargeCounters(0, 19_800, 77),
            20_000);

        var accepted = ButlerChargeRules.TryQuotePaidProductionCostCharge(request, 201, out _, out var failure);
        await Assert.That(accepted).IsFalse();
        await Assert.That(failure).IsEqualTo(ButlerChargeFailure.WeeklyProductionCostQuotaExceeded);

        accepted = ButlerChargeRules.TryQuotePaidProductionCostCharge(request, 200, out var quote, out failure);
        await Assert.That(accepted).IsTrue();
        await Assert.That(failure).IsEqualTo(ButlerChargeFailure.None);
        await Assert.That(quote.NewCounters.WeeklyChargedAmount).IsEqualTo(20_000UL);
    }

    [Test]
    public async Task WeeklyReset_UsesTheConfiguredSundayNumbering()
    {
        var saturday = new DateTime(2026, 9, 12, 23, 59, 0, DateTimeKind.Unspecified);
        var sunday = saturday.AddMinutes(1);

        await Assert.That(ButlerChargeRules.WeeklyResetStart(sunday, ButlerWeekday.Sunday))
            .IsEqualTo(new DateTime(2026, 9, 13));
        await Assert.That(ButlerChargeRules.RequiresWeeklyReset(saturday, sunday, ButlerWeekday.Sunday)).IsTrue();
    }

    [Test]
    public async Task ContentConfig_ReadsEveryChargeLimitByItsVerifiedName()
    {
        var data = ContentConfigGameData.Instance;
        data.SetForTest(ButlerContentConfig.ProductionCostWeeklyChargeAmountLimit, 11);
        data.SetForTest(ButlerContentConfig.ProductionCostWeeklyFreeChargeLimit, 12);
        data.SetForTest(ButlerContentConfig.ProductionCostFreeChargeAmount, 13);
        data.SetForTest(ButlerContentConfig.LpDailyChargeAmountLimit, 14);
        data.SetForTest(ButlerContentConfig.LpChargeMinimum, 15);
        data.SetForTest(ButlerContentConfig.ProductionCostWeeklyFreeChargeResetDay, 1);

        var config = ButlerContentConfig.RequireChargeLimits();

        await Assert.That(config).IsEqualTo(new ButlerChargeContentConfig(11, 12, 13, 14, 15, ButlerWeekday.Sunday));
    }

    [Test]
    public async Task ContentConfig_RejectsNegativeOrZeroChargeLimits()
    {
        var data = ContentConfigGameData.Instance;
        data.SetForTest(ButlerContentConfig.LpChargeMinimum, -1);

        var threw = false;
        try
        {
            _ = ButlerContentConfig.MinimumLaborPowerChargeAmount;
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }
}
