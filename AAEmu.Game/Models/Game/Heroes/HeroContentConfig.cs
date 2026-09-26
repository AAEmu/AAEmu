using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Heroes;

/// <summary>Names of the <c>content_configs</c> rows the Hero system reads.</summary>
public static class HeroContentConfig
{
    public const string MobilizationOrderLevel = "mobilization_order_level";
    public const string MobilizationOrderLeadershipPoint = "mobilization_order_leadership_point";
    public const string MobilizationOrderUiOpenSecond = "mobilization_order_ui_open_second";
    public const string MobilizationOrderAcceptDelay = "mobilization_order_accept_delay";
    public const string MobilizationOrderDailyCountMax = "mobilization_order_daily_count_max";
    public const string MobilizationOrderGiveItem = "mobilization_order_give_item";
    public const string HeroDominionDailyLimit = "hero_dominion_daily_limit";
    public const string HeroDominionPoint = "hero_dominion_point";
    public const string HeroDominionCooldown = "hero_dominion_cooldown";
    public const string HeroDominionTaxRateMin = "hero_dominion_tax_rate_min";
    public const string HeroDominionTaxRateMax = "hero_dominion_tax_rate_max";
    public const string DominionTaxLimit = "dominion_tax_limit";
    public const string DropoutHeroComebackRewardItem = "dropout_hero_comeback_reward_item";

    private static ContentConfigGameData Data => ContentConfigGameData.Instance;

    /// <summary>Level a member needs to accept a Mobilization Order.</summary>
    public static int MobilizationAcceptLevel => Data.GetInt(MobilizationOrderLevel, 0);

    /// <summary>Leadership a member needs to accept a Mobilization Order.</summary>
    public static int MobilizationAcceptLeadership => Data.GetInt(MobilizationOrderLeadershipPoint, 0);

    /// <summary>How long the accept popup stays open on members' screens.</summary>
    public static TimeSpan MobilizationAcceptWindow => TimeSpan.FromSeconds(Data.GetInt(MobilizationOrderAcceptDelay, 0));

    /// <summary>Orders a Hero may issue per UTC day. Zero means the feature is closed.</summary>
    public static int MobilizationDailyMax => Data.GetInt(MobilizationOrderDailyCountMax, 0);

    /// <summary>Item mailed to a member who answers the order; 0 = none.</summary>
    public static uint MobilizationGiveItemId => Data.GetUInt(MobilizationOrderGiveItem, 0);

    /// <summary>Dominion Point gives a Hero may make per UTC day.</summary>
    public static uint DominionPointDailyMax => Data.GetUInt(HeroDominionDailyLimit, 0);

    /// <summary>Points one give credits to the territory.</summary>
    public static int DominionPointValue => Data.GetInt(HeroDominionPoint, 0);

    /// <summary>Cooldown between two gives.</summary>
    public static TimeSpan DominionPointCooldown => TimeSpan.FromSeconds(Data.GetInt(HeroDominionCooldown, 0));

    public static bool TryGetDominionTaxBounds(out int min, out int max)
    {
        if (!Data.TryGet(HeroDominionTaxRateMin, out var minVal) || !Data.TryGet(HeroDominionTaxRateMax, out var maxVal)
            || minVal > int.MaxValue || minVal < int.MinValue
            || maxVal > int.MaxValue || maxVal < int.MinValue)
        {
            min = 0;
            max = 0;
            return false;
        }

        min = (int)minVal;
        max = (int)maxVal;
        return max >= min;
    }

    public static int InitialDominionTaxRate()
    {
        RequireDominionTaxBounds(out var min, out _);
        return min;
    }

    public static bool RequireDominionTaxBounds(out int min, out int max)
    {
        if (!TryGetDominionTaxBounds(out min, out max) || min < 0)
            throw new InvalidOperationException("hero_dominion_tax_rate_min/max are missing or invalid.");
        return true;
    }

    public static bool TryGetDominionTaxLimit(out int limit)
    {
        if (!Data.TryGet(DominionTaxLimit, out var val) || val <= 0 || val > int.MaxValue)
        {
            limit = 0;
            return false;
        }

        limit = (int)val;
        return true;
    }

    public static int RequireDominionTaxLimit()
    {
        if (!TryGetDominionTaxLimit(out var limit))
            throw new InvalidOperationException("dominion_tax_limit is missing or non-positive.");
        return limit;
    }

    public static uint DropoutComebackRewardItemId => Data.GetUInt(DropoutHeroComebackRewardItem, 0);
}
