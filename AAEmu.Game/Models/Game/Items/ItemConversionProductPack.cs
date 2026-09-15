namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// A product pack row (<c>item_conv_ppacks</c>). The pack owns the success chance; its products carry the
/// weights and the output counts.
/// </summary>
public class ItemConversionProductPack
{
    public uint Id { get; init; }
    public string Name { get; init; } = string.Empty;

    /// <summary><c>chance_rate</c>, out of 10000. 10000 on 5656 of 5657 rows.</summary>
    public int ChanceRate { get; init; }

    public List<ItemConversionProduct> Products { get; } = [];

    public int TotalWeight => Products.Sum(product => Math.Max(0, product.Weight));
}
