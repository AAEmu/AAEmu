namespace AAEmu.Game.Models.Game.Butlers;

public enum ButlerChargeFailure
{
    None,
    InvalidAmount,
    InvalidConfiguration,
    InvalidState,
    LaborPowerCapacityReached,
    ProductionCostCapacityReached,
    DailyLaborPowerQuotaExceeded,
    WeeklyProductionCostQuotaExceeded,
    WeeklyFreeChargeQuotaExceeded,
    ArithmeticOverflow
}

public enum ButlerLaborPowerChargePhase
{
    DailyFullRate,
    ReducedRate
}

public readonly record struct ButlerLaborPowerChargeRequest(
    uint PlayerLaborPowerInput,
    uint CurrentButlerLaborPower,
    uint MaximumButlerLaborPower,
    ushort CurrentDailyChargedAmount,
    uint DailyFullRateAmountLimit,
    uint ReducedRateMinimumInput,
    uint ReducedRatePercent);

public readonly record struct ButlerLaborPowerChargeQuote(
    ButlerLaborPowerChargePhase Phase,
    uint PlayerLaborPowerDebit,
    uint ButlerLaborPowerGain,
    uint NewButlerLaborPower,
    ushort NewDailyChargedAmount);

/// <summary>Weekly counters held under the native permanent-data keys 7, 8, and 9.</summary>
public readonly record struct ButlerProductionCostChargeCounters(
    ulong WeeklyFreeChargeCount,
    ulong WeeklyChargedAmount,
    ulong WeeklyResetAnchor);

public readonly record struct ButlerProductionCostChargeRequest(
    uint CurrentProductionCost,
    uint MaximumProductionCost,
    ButlerProductionCostChargeCounters Counters,
    uint WeeklyChargeAmountLimit);

public readonly record struct ButlerProductionCostChargeQuote(
    uint GrantedProductionCost,
    uint NewProductionCost,
    ButlerProductionCostChargeCounters NewCounters);

/// <summary>Pure farmhand labor-power and production-cost charge calculations.</summary>
public static class ButlerChargeRules
{
    // FUN_3918DB20 selects the full-rate phase until the configured daily quota is exhausted.
    private const uint FullRatePercent = 100;

    /// <summary>
    /// Native permanent-data keys used by <c>FUN_39CD7440</c>: free-charge count, weekly charged amount,
    /// and the world-local weekly-reset timestamp.
    /// </summary>
    public const sbyte WeeklyFreeChargeCountPermanentDataKey = 7;
    public const sbyte WeeklyChargedAmountPermanentDataKey = 8;
    public const sbyte WeeklyResetAnchorPermanentDataKey = 9;

    public static bool TryQuoteLaborPowerCharge(
        ButlerLaborPowerChargeRequest request,
        out ButlerLaborPowerChargeQuote quote,
        out ButlerChargeFailure failure)
    {
        quote = default;
        failure = ButlerChargeFailure.None;

        if (request.CurrentButlerLaborPower > request.MaximumButlerLaborPower ||
            request.ReducedRatePercent == 0 || request.ReducedRateMinimumInput == 0)
        {
            failure = ButlerChargeFailure.InvalidConfiguration;
            return false;
        }

        var capacity = request.MaximumButlerLaborPower - request.CurrentButlerLaborPower;
        if (capacity == 0)
        {
            failure = ButlerChargeFailure.LaborPowerCapacityReached;
            return false;
        }

        var phase = request.CurrentDailyChargedAmount < request.DailyFullRateAmountLimit
            ? ButlerLaborPowerChargePhase.DailyFullRate
            : ButlerLaborPowerChargePhase.ReducedRate;
        var remainingDaily = request.DailyFullRateAmountLimit - Math.Min(
            (uint)request.CurrentDailyChargedAmount, request.DailyFullRateAmountLimit);
        var maximumGain = phase == ButlerLaborPowerChargePhase.DailyFullRate
            ? Math.Min(capacity, remainingDaily)
            : capacity;
        var rate = phase == ButlerLaborPowerChargePhase.DailyFullRate ? FullRatePercent : request.ReducedRatePercent;
        var minimumInput = phase == ButlerLaborPowerChargePhase.DailyFullRate ? 1u : request.ReducedRateMinimumInput;

        if (request.PlayerLaborPowerInput < minimumInput)
        {
            failure = ButlerChargeFailure.InvalidAmount;
            return false;
        }

        try
        {
            checked
            {
                var gain = RoundGain(request.PlayerLaborPowerInput, rate);
                if (gain == 0)
                {
                    failure = ButlerChargeFailure.InvalidAmount;
                    return false;
                }

                if (gain > maximumGain)
                {
                    failure = phase == ButlerLaborPowerChargePhase.DailyFullRate && remainingDaily < capacity
                        ? ButlerChargeFailure.DailyLaborPowerQuotaExceeded
                        : ButlerChargeFailure.LaborPowerCapacityReached;
                    return false;
                }

                // Server policy: the client proves phase selection but not backend counter mutation.
                // Keep the exhausted counter stable in reduced mode so future quotes stay reduced-rate.
                var newDailyCharged = phase == ButlerLaborPowerChargePhase.DailyFullRate
                    ? checked((uint)request.CurrentDailyChargedAmount + gain)
                    : request.CurrentDailyChargedAmount;
                if (newDailyCharged > ushort.MaxValue)
                {
                    failure = ButlerChargeFailure.ArithmeticOverflow;
                    return false;
                }

                quote = new ButlerLaborPowerChargeQuote(
                    phase,
                    request.PlayerLaborPowerInput,
                    gain,
                    request.CurrentButlerLaborPower + gain,
                    (ushort)newDailyCharged);
                return true;
            }
        }
        catch (OverflowException)
        {
            failure = ButlerChargeFailure.ArithmeticOverflow;
            return false;
        }
    }

    /// <summary>Quotes the packet kind-1 free production-cost grant. The client sends amount zero.</summary>
    public static bool TryQuoteFreeProductionCostCharge(
        ButlerProductionCostChargeRequest request,
        uint weeklyFreeChargeLimit,
        uint freeChargeAmount,
        out ButlerProductionCostChargeQuote quote,
        out ButlerChargeFailure failure) =>
        TryQuoteProductionCostCharge(request, freeChargeAmount, weeklyFreeChargeLimit, true, out quote, out failure);

    /// <summary>
    /// Quotes a paid production-cost grant. The requested amount is the resolved type-185 special-effect value,
    /// not an item template ID.
    /// </summary>
    public static bool TryQuotePaidProductionCostCharge(
        ButlerProductionCostChargeRequest request,
        uint requestedProductionCost,
        out ButlerProductionCostChargeQuote quote,
        out ButlerChargeFailure failure) =>
        TryQuoteProductionCostCharge(request, requestedProductionCost, 0, false, out quote, out failure);

    /// <summary>Returns the local midnight at the configured farmhand weekly reset day.</summary>
    public static DateTime WeeklyResetStart(DateTime localTime, ButlerWeekday resetDay)
    {
        if (!Enum.IsDefined(resetDay))
            throw new ArgumentOutOfRangeException(nameof(resetDay));

        var day = localTime.Date;
        var target = (int)resetDay - (int)ButlerWeekday.Sunday;
        var offset = ((int)day.DayOfWeek - target + 7) % 7;
        return DateTime.SpecifyKind(day.AddDays(-offset), localTime.Kind);
    }

    /// <summary>Whether world-local time passed into a later configured weekly reset period.</summary>
    public static bool RequiresWeeklyReset(DateTime resetAnchorLocalTime, DateTime currentLocalTime, ButlerWeekday resetDay) =>
        WeeklyResetStart(resetAnchorLocalTime, resetDay) < WeeklyResetStart(currentLocalTime, resetDay);

    private static bool TryQuoteProductionCostCharge(
        ButlerProductionCostChargeRequest request,
        uint requestedProductionCost,
        uint weeklyFreeChargeLimit,
        bool isFree,
        out ButlerProductionCostChargeQuote quote,
        out ButlerChargeFailure failure)
    {
        quote = default;
        failure = ButlerChargeFailure.None;

        if (request.CurrentProductionCost > request.MaximumProductionCost)
        {
            failure = ButlerChargeFailure.InvalidState;
            return false;
        }

        if (requestedProductionCost == 0)
        {
            failure = ButlerChargeFailure.InvalidAmount;
            return false;
        }

        if (isFree && request.Counters.WeeklyFreeChargeCount >= weeklyFreeChargeLimit)
        {
            failure = ButlerChargeFailure.WeeklyFreeChargeQuotaExceeded;
            return false;
        }

        var capacity = request.MaximumProductionCost - request.CurrentProductionCost;
        if (capacity == 0)
        {
            failure = ButlerChargeFailure.ProductionCostCapacityReached;
            return false;
        }

        var weeklyRemaining = request.Counters.WeeklyChargedAmount >= request.WeeklyChargeAmountLimit
            ? 0
            : request.WeeklyChargeAmountLimit - (uint)request.Counters.WeeklyChargedAmount;
        if (weeklyRemaining == 0)
        {
            failure = ButlerChargeFailure.WeeklyProductionCostQuotaExceeded;
            return false;
        }

        var granted = isFree
            ? Math.Min(requestedProductionCost, Math.Min(weeklyRemaining, capacity))
            : requestedProductionCost;
        if (!isFree && granted > Math.Min(weeklyRemaining, capacity))
        {
            failure = granted > capacity
                ? ButlerChargeFailure.ProductionCostCapacityReached
                : ButlerChargeFailure.WeeklyProductionCostQuotaExceeded;
            return false;
        }

        try
        {
            checked
            {
                var counters = request.Counters with
                {
                    WeeklyChargedAmount = request.Counters.WeeklyChargedAmount + granted,
                    WeeklyFreeChargeCount = isFree
                        ? request.Counters.WeeklyFreeChargeCount + 1
                        : request.Counters.WeeklyFreeChargeCount
                };
                quote = new ButlerProductionCostChargeQuote(
                    granted,
                    request.CurrentProductionCost + granted,
                    counters);
                return true;
            }
        }
        catch (OverflowException)
        {
            failure = ButlerChargeFailure.ArithmeticOverflow;
            return false;
        }
    }

    private static uint RoundGain(uint input, uint ratePercent) =>
        checked((uint)(((ulong)input * ratePercent + FullRatePercent / 2) / FullRatePercent));
}
