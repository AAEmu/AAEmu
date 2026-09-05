using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Game;

/// <summary>
/// Session grants that must travel with <c>SCAccountAttributeList</c>.
/// </summary>
/// <remarks>
/// Compact ships no <c>auction_post</c> rows. The client still treats that kind as live
/// once <see cref="AccountAttributeConfigRules"/> marks it used, and then refuses a post
/// unless the list carries extraKind 0 (or the account already has that row).
/// </remarks>
public static class AccountAttributeGrantRules
{
    public const uint ListingExtraKind = 0;

    public static AccountAttribute CreateListingGrant(uint accountId) => new()
    {
        AccountId = accountId,
        KindId = (uint)AccountAttributeKind.AuctionPost,
        KindValue = ListingExtraKind,
        WorldId = 0,
        Count = 1,
        Starts = DateTime.UnixEpoch,
        Expires = DateTime.UnixEpoch
    };

    public static void EnsureListingGrant(IList<AccountAttribute> attributes, uint accountId)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        if (!AccountAttributeConfigRules.KindIsUsed((byte)AccountAttributeKind.AuctionPost))
            return;

        if (attributes.Any(a => a.KindId == (uint)AccountAttributeKind.AuctionPost &&
                                a.KindValue == ListingExtraKind))
            return;

        attributes.Add(CreateListingGrant(accountId));
    }
}
