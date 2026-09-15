namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// A conversion family (<c>item_conv_sets</c>). The dialog text is what the client shows before the player
/// confirms, and the id is what the ItemConversion special effect names in its first value.
/// </summary>
public class ItemConversionSet
{
    public uint Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string DialogTitle { get; init; } = string.Empty;
    public string DialogContent { get; init; } = string.Empty;

    /// <summary>Conversions (<c>item_convs.id</c>) that belong to this family.</summary>
    public List<uint> ConversionIds { get; } = [];
}

/// <summary>
/// Result of a conversion roll: the chosen product (null when the pack's chance failed) and how many of it
/// to hand out. A failed chance is still a consumed reagent, so the caller needs both facts.
/// </summary>
public sealed class ItemConversionRoll
{
    public ItemConversionProduct Product { get; init; }
    public int Count { get; init; }

    public bool ChanceFailed => Product == null;
}
