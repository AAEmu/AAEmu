namespace AAEmu.Game.Models.Game.FishSchools;

/// <summary>
/// Stand payout for a caught-fish backpack: grade-adjust the catalog refund, then scale by
/// measured weight over the species max. Both steps use floor(x + 0.5). A zero or unknown
/// weight must not produce a successful sale.
/// </summary>
public static class FishSalePrice
{
    public static bool TryCalculate(int refund, int refundMultiplier, float weight, int maxWeight, out long price)
    {
        const float percentScale = 0.01f;

        price = 0;
        if (refund < 0 || refundMultiplier < 0 || weight <= 0 || !float.IsFinite(weight) || maxWeight <= 0)
            return false;

        var adjustedBaseValue = refundMultiplier * percentScale * refund;
        if (!float.IsFinite(adjustedBaseValue))
            return false;
        var adjustedBase = (long)MathF.Floor(adjustedBaseValue + 0.5f);

        var weightedValue = (float)adjustedBase * weight / maxWeight;
        if (!float.IsFinite(weightedValue) || weightedValue <= 0)
            return false;

        price = (long)MathF.Floor(weightedValue + 0.5f);
        return price > 0;
    }
}
