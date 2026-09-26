namespace AAEmu.Game.Models.Game.Trading;

/// <summary>
/// A prepared farmhand specialty-trade delivery: the market write plus every payout figure the
/// owner's letter needs, all resolved from shipped content at the moment the delivery was prepared.
/// </summary>
/// <param name="Market">The delivery's market write, not yet durable.</param>
/// <param name="ProductItemId">The craft product that was delivered.</param>
/// <param name="NpcTemplateId">The destination specialty NPC the product was sold at.</param>
/// <param name="ZoneGroupId">The destination zone group the route ratio belongs to.</param>
/// <param name="BasePrice">The bundle base price in money, before ratio, freshness and event scaling.</param>
/// <param name="DisplayedRatioPercent">
/// The route ratio in percent as it stood <em>before</em> this delivery was applied to the market.
/// A delivery is paid at the price it was accepted at, not at the price it leaves behind for the
/// next one, so this must never be re-read after the market write lands.
/// </param>
/// <param name="FreshnessRewardRate">The freshness-group reward rate in wire units (1000 = neutral).</param>
/// <param name="EventMultiplier">The active specialty event multiplier applied to the payout.</param>
/// <param name="InterestPercent">The mail interest percent applied to the payout.</param>
/// <param name="FreshnessPercent">The freshness reward rate as the percent the letter body reports.</param>
/// <param name="SpecialtyMerchantRatioPercent">The event multiplier as the percent the letter body reports.</param>
/// <param name="SellerSharePercent">The seller share as the percent the letter body reports.</param>
/// <param name="CoinItemTemplateId">The template the payout is delivered in (coins unless the NPC names another).</param>
/// <param name="TotalPayout">The owner payout, expressed in <paramref name="CoinItemTemplateId"/> units.</param>
/// <param name="PayoutBeforeInterest">The payout before mail interest, in the same units.</param>
/// <param name="BasePayout">The base price converted to the same units, for the letter body.</param>
public sealed record ButlerSpecialtyTradeDeliveryQuote(
    SpecialtyMarketWrite Market,
    uint ProductItemId,
    uint NpcTemplateId,
    uint ZoneGroupId,
    int BasePrice,
    int DisplayedRatioPercent,
    uint FreshnessRewardRate,
    float EventMultiplier,
    double InterestPercent,
    double FreshnessPercent,
    int SpecialtyMerchantRatioPercent,
    int SellerSharePercent,
    uint CoinItemTemplateId,
    int TotalPayout,
    int PayoutBeforeInterest,
    int BasePayout);
