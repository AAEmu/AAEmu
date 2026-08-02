namespace AAEmu.Game.Models.Game;

/// <summary>
/// Account attribute domains from <c>enum_account_attribute_kinds</c> in game content.
/// The wire representation is the native <c>i8 AccountAttributeKind</c> field.
/// </summary>
public enum AccountAttributeKind : byte
{
    AuctionPost = 1,
    AccountBuff = 2,
    Ulc = 3
}
