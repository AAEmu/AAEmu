namespace AAEmu.Game.Models.Game.CashShop;

/// <summary>
/// Table: ics_menu
/// </summary>
public class IcsMenu
{
    /// <summary>
    /// Auto-Increment unique value to be able to use EF
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Main Tab this item is on
    /// </summary>
    public byte MainTab { get; set; }

    /// <summary>
    /// Sub Tab this item is on
    /// </summary>
    public byte SubTab { get; set; }

    /// <summary>
    /// Number used for item order on the sub tab
    /// </summary>
    public int TabPos { get; set; }

    /// <summary>
    /// Reference to the Shop Item listing
    /// </summary>
    public IcsItem ShopItem { get; set; }
}
