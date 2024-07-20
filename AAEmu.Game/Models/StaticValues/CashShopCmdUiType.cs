namespace AAEmu.Game.Models.StaticValues;

/// <summary>
/// What buttons are available in the cash shop, any other value fully disables all buttons.
/// Note that the featured tab does not allow gift or cart either way.
/// </summary>
public enum CashShopCmdUiType : byte
{
    AllowAll = 0,
    NoCartAllowed = 1,
    NoGiftAllowed = 2,
    OnlyBuyAllowed = 3,
}
