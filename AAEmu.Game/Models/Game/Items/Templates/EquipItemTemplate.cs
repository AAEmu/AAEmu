namespace AAEmu.Game.Models.Game.Items.Templates;

public class EquipItemTemplate : ItemTemplate
{
    public override Type ClassType => typeof(EquipItem);

    public uint ModSetId { get; set; }
    public bool Repairable { get; set; }
    public int DurabilityMultiplier { get; set; }
    public uint RechargeBuffId { get; set; }
    public int ChargeLifetime { get; set; }
    public int ChargeCount { get; set; } // does not seem to be actually used anywhere in the DB
    public uint RechargeRestrictItemId { get; set; }
    public ItemLookConvert ItemLookConvert { get; set; }
    public uint EquipItemSetId { get; set; }
    /// <summary>Default packed ARGB color from 10.0.2.13 <c>dyeable_items.color</c>.</summary>
    public uint DyeingColor { get; set; }
    /// <summary>
    /// Synthesis ("Item Growth") category, from <c>item_rnd_attr_category_id</c>. 0 when the piece
    /// cannot be synthesized at all.
    /// </summary>
    public uint ItemRndAttrCategoryId { get; set; }
}
