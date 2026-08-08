using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Premium;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Which paid memberships an account currently holds, and what they add to its labor.
/// </summary>
/// <remarks>
/// One place on purpose: the same list has to reach the client (as account attributes, which is the
/// only thing it accepts as proof of membership - see <see cref="AccountMembership"/>) and the labor
/// arithmetic. When the two drifted apart the client showed benefits the server never paid out.
/// </remarks>
public static class AccountMemberships
{
    /// <summary>
    /// Membership ids active for an account: the account_buff attributes it holds, plus the ones
    /// Account.ForceMaxPremiumGrade hands to everybody.
    /// </summary>
    public static List<uint> ActiveIds(uint accountId, uint worldId)
    {
        var ids = AccountAttributeManager.Instance
            .Get(accountId, worldId)
            .Where(a => a.KindId == (uint)AccountAttributeKind.AccountBuff)
            .Select(a => a.KindValue)
            .ToList();

        if (AppConfiguration.Instance.Account?.ForceMaxPremiumGrade != true)
            return ids;

        foreach (var forced in ForcedIds)
        {
            if (!ids.Contains(forced))
                ids.Add(forced);
        }

        return ids;
    }

    /// <summary>The memberships a forced max grade grants. Both, so nothing is held back.</summary>
    public static IReadOnlyList<uint> ForcedIds { get; } =
        [(uint)AccountMembership.Ancient, (uint)AccountMembership.Advanced];

    /// <summary>
    /// The grade's labor numbers with the account's memberships applied - the same sum the client
    /// computes for its own display.
    /// </summary>
    public static LaborAllowance LaborFor(uint premiumGradeId, uint accountId, uint worldId)
    {
        var grade = PremiumGameData.Instance.GetGrade(premiumGradeId);
        return AccountBuffsGameData.Instance.Apply(grade, ActiveIds(accountId, worldId));
    }
}
