namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// One step of the tempering ladder (<c>enchant_scale_ratios</c>). The row id is the scale the item
/// currently sits at, and the ratios on it decide what happens when the player tries to push it one
/// step further - row 0 is "+0", row 12 is "+12" and so on up to "+30".
/// </summary>
/// <remarks>
/// All ratios are per 10000. Shipped data keeps <c>Break</c> and <c>Disable</c> at zero everywhere
/// and only starts applying <c>Down</c> from +18 upward, so the practical failure mode below +18 is
/// "nothing happens".
/// </remarks>
public class EnchantScaleRatio
{
    public byte Id { get; set; }

    /// <summary>Display name the client shows on the item ("+7", or "none" for row 0).</summary>
    public string Name { get; set; }

    /// <summary>Scale value carried by this step; the shipped ladder runs 0, 10, 20 … 250.</summary>
    public short Scale { get; set; }

    public int SuccessRatio { get; set; }

    /// <summary>Chance, rolled only after a success, that the step counts double.</summary>
    public int GreatSuccessRatio { get; set; }

    public int BreakRatio { get; set; }
    public int DisableRatio { get; set; }
    public int DownRatio { get; set; }

    /// <summary>How many steps a "down" failure drops.</summary>
    public byte DownMax { get; set; }

    public int Cost { get; set; }
    public uint CurrencyId { get; set; }
}
