namespace AAEmu.Game.Models.Game.Items.Templates;

public class ItemBagTemplate : ItemTemplate
{
    public override Type ClassType => typeof(ItemBag);

    /// <summary>Primary key from item_bags; coffer restrictions and unit requirements reference it.</summary>
    public uint ItemBagId { get; set; }
    public int Capacity { get; set; }
    public bool OrUnitReqs { get; set; }
    public HashSet<int> AllowedItemCategoryIds { get; } = [];
}
