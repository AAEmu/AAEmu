namespace AAEmu.Game.Models.Game;

/// <summary>
/// <c>SCAccountAttributeConfig</c> is four <c>used</c> bytes, one per
/// <see cref="AccountAttributeKind"/> slot (index 0 is reserved).
/// </summary>
/// <remarks>
/// A used kind means this world offers that domain. Auction listing then also
/// needs extraKind 0 on <c>SCAccountAttributeList</c> — see
/// <see cref="AccountAttributeGrantRules"/>. Turning the kind off does not
/// unlock posting: the in-world player still treats the domain as live.
/// </remarks>
public static class AccountAttributeConfigRules
{
    public const int KindSlotCount = 4;

    public static bool KindIsUsed(byte slot) => slot switch
    {
        (byte)AccountAttributeKind.AuctionPost => true,
        (byte)AccountAttributeKind.AccountBuff => true,
        (byte)AccountAttributeKind.Ulc => true,
        _ => false
    };

    public static IReadOnlyList<bool> UsedFlags { get; } =
    [
        KindIsUsed(0),
        KindIsUsed((byte)AccountAttributeKind.AuctionPost),
        KindIsUsed((byte)AccountAttributeKind.AccountBuff),
        KindIsUsed((byte)AccountAttributeKind.Ulc)
    ];
}
