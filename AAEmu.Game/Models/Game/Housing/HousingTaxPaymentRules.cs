namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// How housing tax copper converts to the currency the client charges.
/// Guild residences (<c>hs_expedition_house*</c>) pay prestige via
/// <c>content_configs.contribution_gold_ratio</c>; ordinary houses with the tax-item feature on
/// pay certificates via <c>tax_gold_ratio</c>.
/// </summary>
public static class HousingTaxPaymentRules
{
    public const string ContributionGoldRatioKey = "contribution_gold_ratio";
    public const string TaxGoldRatioKey = "tax_gold_ratio";

    /// <summary>Wire <c>taxType</c> for <c>HOUSING_TAX_SEAL</c> (tax certificates).</summary>
    /// <remarks>Client Lua global <c>HOUSING_TAX_SEAL</c> = 1.</remarks>
    public const byte TaxTypeSeal = 1;

    /// <summary>Wire <c>taxType</c> for <c>HOUSING_TAX_CONTRIBUTION</c> (prestige).</summary>
    /// <remarks>
    /// Client Lua global <c>HOUSING_TAX_CONTRIBUTION</c> = 2. Value 0 is not a tax type: the client's
    /// <c>CountTaxItemForTax</c> returns the copper amount unchanged for any type other than 1/2, and
    /// the maintain/mail UI still paints non-seal amounts as prestige — so sending 0 made guild tax
    /// look like 200000 prestige while the debit correctly used <c>contribution_gold_ratio</c>.
    /// </remarks>
    public const byte TaxTypeContribution = 2;

    public static bool UsesContributionTax(bool isExpeditionResidence) => isExpeditionResidence;

    /// <summary>
    /// Client tax-type byte for maintain/mail UI. Expedition residences always report contribution;
    /// other houses follow the tax-item feature (seal vs the non-seal path).
    /// </summary>
    public static byte ResolveTaxType(bool isExpeditionResidence, bool taxItemFeature) =>
        isExpeditionResidence
            ? TaxTypeContribution
            : taxItemFeature
                ? TaxTypeSeal
                : TaxTypeContribution;

    /// <summary>
    /// Units of the active tax currency for a copper tax amount. Same ceiling the client uses via
    /// <c>X2House:CountTaxItemForTax</c> with the matching gold ratio.
    /// </summary>
    public static int CurrencyUnits(int taxCopper, int goldRatio)
    {
        if (taxCopper <= 0)
            return 0;
        if (goldRatio <= 0)
            throw new ArgumentOutOfRangeException(nameof(goldRatio), goldRatio, "Tax gold ratio must be positive.");
        return Math.Max(1, (int)Math.Ceiling(taxCopper / (double)goldRatio));
    }
}
