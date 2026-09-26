using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Dominions;

/// <summary>
/// Content lookup rules for the shipped <c>siege_extortion_ratios</c> table. The table names the
/// faction/count key and ratio, but the available 10.0.2.13 evidence does not prove which consumer
/// applies it, so this slice deliberately stops at an exact, fail-loud lookup.
/// </summary>
public static class SiegeExtortionRules
{
    public static bool TryResolve(
        IEnumerable<SiegeExtortionRatio> rows,
        uint factionId,
        uint dominionCount,
        out SiegeExtortionRatio ratio)
    {
        var matches = (rows ?? []).Where(row => row.FactionId == factionId && row.DominionCount == dominionCount).ToArray();
        if (matches.Length == 1)
        {
            ratio = matches[0];
            return true;
        }

        ratio = null;
        return false;
    }

    public static int RequireRatio(
        IEnumerable<SiegeExtortionRatio> rows,
        uint factionId,
        uint dominionCount) =>
        TryResolve(rows, factionId, dominionCount, out var ratio)
            ? ratio.Ratio
            : throw new InvalidOperationException(
                $"No unique siege_extortion_ratios row for faction {factionId} / dominion count {dominionCount}.");
}
