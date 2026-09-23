namespace AAEmu.Game.Models.Game.Merchant;

/// <summary>
/// The weighted dice behind random merchant stock and (later) reopen-box rolls: content rows
/// carry a weight column on every selectable row, and the window draws from them.
/// </summary>
/// <remarks>
/// Two shapes cover both flows: <see cref="PickIndex"/> for a single die over one pool, and
/// <see cref="PickDistinctIndexes"/> for filling a window with distinct offers (random shop
/// draws <c>merchant_random_packs.sale_cnt</c> groups without replacement - sale_cnt never
/// exceeds the group count in any of the 7 shipped packs, and equals it exactly for packs 3
/// and 5 whose group weights are all 1, which only reads as "show every group once").
/// Weights are per-million-ish integers from content (pack 1 groups sum to exactly 1,000,000),
/// so arithmetic stays in long.
/// </remarks>
public static class WeightedSelection
{
    /// <summary>One weighted die over <paramref name="weights"/>. Returns -1 for an empty or all-zero pool.</summary>
    public static int PickIndex(IReadOnlyList<long> weights, Random rng)
    {
        if (weights.Count == 0)
            return -1;

        long total = 0;
        foreach (var w in weights)
        {
            if (w > 0)
                total += w;
        }
        if (total <= 0)
            return -1;

        var roll = (long)(rng.NextDouble() * total);
        for (var i = 0; i < weights.Count; i++)
        {
            if (weights[i] <= 0)
                continue;
            if (roll < weights[i])
                return i;
            roll -= weights[i];
        }

        // Unreachable for a roll in [0, total), but never invent a pick when arithmetic disagrees.
        return -1;
    }

    /// <summary>
    /// Fills <paramref name="count"/> slots from distinct pool entries, each die drawn from the
    /// weights that are still on the table (sequential weighted sampling without replacement).
    /// Zero-weight entries are never drawn. When fewer than <paramref name="count"/> entries can
    /// be drawn, all drawable entries are returned rather than failing or wrapping.
    /// </summary>
    public static List<int> PickDistinctIndexes(IReadOnlyList<long> weights, int count, Random rng)
    {
        var picked = new List<int>(Math.Max(0, count));
        if (count <= 0 || weights.Count == 0)
            return picked;

        var remaining = new long[weights.Count];
        for (var i = 0; i < weights.Count; i++)
            remaining[i] = weights[i] > 0 ? weights[i] : 0;

        for (var draw = 0; draw < count; draw++)
        {
            var index = PickIndex(remaining, rng);
            if (index < 0)
                break;
            picked.Add(index);
            remaining[index] = 0;
        }

        return picked;
    }
}
