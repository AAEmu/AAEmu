namespace AAEmu.Game.Models.Game.Dominions;

/// <summary>
/// Pure claim/tax decisions. Lodestone templates and tax bounds come from
/// <c>housings</c> / <c>housing_build_steps</c> / <c>content_configs</c> — nothing here names a zone group
/// or an item.
/// </summary>
public static class DominionClaimRules
{
    /// <summary>
    /// A housing template is a claimable Guard Tower when it has a <c>guard_tower_settings</c> row
    /// or a build step whose skill applies the DeclareDominion special effect.
    /// </summary>
    public static bool IsLodestoneTemplate(uint guardTowerSettingId, bool buildStepDeclaresDominion) =>
        guardTowerSettingId > 0 || buildStepDeclaresDominion;

    /// <summary>
    /// First live house in <paramref name="zoneGroupId"/> whose template is a lodestone. Placement is the
    /// seeded house, not a static zone→template map.
    /// </summary>
    public static T FindLodestoneInZone<T>(
        IEnumerable<T> houses,
        ushort zoneGroupId,
        Func<T, ushort> zoneGroupOf,
        Func<T, bool> isLodestone)
    {
        foreach (var house in houses)
        {
            if (house == null || !isLodestone(house))
                continue;
            if (zoneGroupOf(house) == zoneGroupId)
                return house;
        }

        return default;
    }

    /// <summary>
    /// <c>content_configs.hero_dominion_tax_rate_min/max</c>. Missing bounds close the setter rather than
    /// inventing a range.
    /// </summary>
    public static bool IsTaxRateAllowed(int taxRate, int min, int max) =>
        max >= min && taxRate >= min && taxRate <= max;

    /// <summary>Opening tax for a new claim: the configured minimum, or 0 when bounds are absent.</summary>
    public static int InitialTaxRate(bool hasBounds, int min) => hasBounds ? min : 0;

    /// <summary>
    /// Weekly payout follows <c>siege_plans.week_start</c>. No current week for the zone group means
    /// the window is closed — do not invent a 7-day timer.
    /// </summary>
    public static bool ShouldPayOnSiegeWeek(DateTime lastPaidUtc, DateTime? currentWeekStartUtc) =>
        currentWeekStartUtc is { } week && lastPaidUtc < week;

    /// <summary>
    /// <c>content_configs.dominion_tax_limit</c>. The limit is required content: a missing or
    /// non-positive value is a startup/configuration error, never permission to pay the full pool.
    /// </summary>
    public static long CapTax(long total, long limit)
    {
        if (limit <= 0)
            throw new InvalidOperationException("dominion_tax_limit must be a positive content value.");
        if (total <= 0)
            return 0;
        return total < limit ? total : limit;
    }

    /// <summary>
    /// House / hunt / peace left after mailing <paramref name="payable"/>. Drains house, then hunt, then
    /// peace. A cap must not wipe the unpaid remainder.
    /// </summary>
    public static (long House, long Hunt, long Peace) AfterTaxPayout(long house, long hunt, long peace, long payable)
    {
        var left = payable < 0 ? 0 : payable;
        house = DrainTaxBucket(house, ref left);
        hunt = DrainTaxBucket(hunt, ref left);
        peace = DrainTaxBucket(peace, ref left);
        return (house, hunt, peace);
    }

    public readonly record struct TaxPool(int House, int Hunt, int Peace, DateTime LastPaid);

    /// <summary>Drains the pool and stamps last-paid so a later tick cannot pay the same week again.</summary>
    public static TaxPool SettleTaxPool(TaxPool pool, int payable, DateTime paidAt)
    {
        var remaining = AfterTaxPayout(pool.House, pool.Hunt, pool.Peace, payable);
        return new((int)remaining.House, (int)remaining.Hunt, (int)remaining.Peace, paidAt);
    }

    /// <summary>A failed send must not keep the settlement; a successful send keeps the claimed week.</summary>
    public static TaxPool AfterTaxMail(TaxPool settled, TaxPool beforeSend, bool sendSucceeded) =>
        sendSucceeded ? settled : beforeSend;

    private static long DrainTaxBucket(long amount, ref long payable)
    {
        if (amount <= 0 || payable <= 0)
            return amount < 0 ? 0 : amount;
        var take = amount < payable ? amount : payable;
        payable -= take;
        return amount - take;
    }

    /// <summary>
    /// Weekly mail amount: 0 when the siege week has not rolled, otherwise the capped pool.
    /// </summary>
    public static long TaxDue(DateTime lastPaidUtc, DateTime? currentWeekStartUtc, long pool, long limit) =>
        ShouldPayOnSiegeWeek(lastPaidUtc, currentWeekStartUtc) ? CapTax(pool, limit) : 0;

    /// <summary>
    /// One of the five <c>dominion_housings</c> designs may exist once per claimed zone group.
    /// Walls, gates, and towers are not unique.
    /// </summary>
    public static bool MayPlaceUniqueDominionDesign(bool isUniqueDesign, bool alreadyPresent) =>
        !isUniqueDesign || !alreadyPresent;

    public static bool HasDesignInZone<T>(
        IEnumerable<T> houses,
        uint designId,
        ushort zoneGroupId,
        Func<T, uint> designOf,
        Func<T, ushort> zoneGroupOf)
    {
        foreach (var house in houses)
        {
            if (house == null || designOf(house) != designId)
                continue;
            if (zoneGroupOf(house) == zoneGroupId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Skill 13661 gates, in the order the client is told about them. Silent refusals stay
    /// <see cref="DominionDeclareRefuse.NoExpedition"/> and <see cref="DominionDeclareRefuse.NotALodestone"/>.
    /// </summary>
    public static DominionDeclareRefuse GetDeclareRefuse(
        bool zoneLocked,
        bool isFactionTerritory,
        bool isCurrentHero,
        bool hasOwningFaction,
        bool hasExpedition,
        bool windowOpen,
        bool alreadyClaimed,
        bool lodestoneOk)
    {
        if (zoneLocked)
            return DominionDeclareRefuse.Locked;
        if (isFactionTerritory)
        {
            if (!isCurrentHero)
                return DominionDeclareRefuse.NotHero;
            if (!hasOwningFaction)
                return DominionDeclareRefuse.NoOwningFaction;
        }
        else if (!hasExpedition)
            return DominionDeclareRefuse.NoExpedition;

        // Guild territories have no siege_zones schedule, so the declare window is a faction-only gate.
        if (isFactionTerritory && !windowOpen)
            return DominionDeclareRefuse.WindowClosed;
        if (alreadyClaimed)
            return DominionDeclareRefuse.AlreadyClaimed;
        if (!lodestoneOk)
            return DominionDeclareRefuse.NotALodestone;
        return DominionDeclareRefuse.None;
    }

    public static ErrorMessageType? MessageFor(DominionDeclareRefuse refuse) => refuse switch
    {
        DominionDeclareRefuse.None => null,
        DominionDeclareRefuse.Locked => ErrorMessageType.Invalid,
        DominionDeclareRefuse.NotHero or DominionDeclareRefuse.NoOwningFaction => ErrorMessageType.NoPerm,
        DominionDeclareRefuse.NoExpedition => null,
        DominionDeclareRefuse.WindowClosed => ErrorMessageType.DominionNotDeclareTime,
        DominionDeclareRefuse.AlreadyClaimed => ErrorMessageType.DominionAlreadyDedclared,
        DominionDeclareRefuse.NotALodestone => null,
        _ => ErrorMessageType.Invalid
    };
}

public enum DominionDeclareRefuse
{
    None = 0,
    Locked,
    NotHero,
    NoOwningFaction,
    NoExpedition,
    WindowClosed,
    AlreadyClaimed,
    NotALodestone
}
