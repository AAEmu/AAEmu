namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// One product row (<c>item_conv_products</c>) of a <c>item_conv_ppacks</c> product pack.
/// </summary>
public class ItemConversionProduct
{
    /// <summary><c>item_conv_ppacks.id</c> this product belongs to.</summary>
    public uint ProductPackId;

    /// <summary><c>item_conv_ppacks.chance_rate</c>, out of 10000. 10000 on every pack but one.</summary>
    public int ChanceRate;

    public uint OutputItemId;

    /// <summary>
    /// <c>item_conv_products.weight</c> for the weighted pick between the products of one pack. Six packs in
    /// 10.0.2.13 carry more than one product (22 rows); their weights mix 1 with 2, 3 or 4, and pack 5571
    /// pairs a weight-1 product with a weight-4 one.
    /// </summary>
    public int Weight;

    public int MinOutput;
    public int MaxOutput;

    /// <summary><c>item_conv_products.item_grade_id</c>; -1 and 0 both mean "let the template decide".</summary>
    public int GradeId = -1;
}
