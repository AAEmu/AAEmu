using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Items.Templates;

public class ItemTemplate
{
    public virtual Type ClassType => typeof(Item);

    public uint Id { get; set; }
    /// <summary>
    /// Original Korean name is stored here, use LocalizationManager to get the names for other languages
    /// </summary>
    public string Name { get; set; }
    public int CategoryId { get; set; }
    public int Level { get; set; }
    public int Price { get; set; }
    public int Refund { get; set; }
    public ItemBindType BindType { get; set; }
    public int PickupLimit { get; set; }
    public int MaxCount { get; set; }
    public bool Sellable { get; set; }
    public uint UseSkillId { get; set; }
    public bool UseSkillAsReagent { get; set; }
    public ItemImplEnum ImplId { get; set; }
    public uint BuffId { get; set; }
    public bool Gradable { get; set; }
    public bool LootMulti { get; set; }
    public uint LootQuestId { get; set; }
    public int HonorPrice { get; set; }
    public int ExpAbsLifetime { get; set; }
    public int ExpOnlineLifetime { get; set; }
    public DateTime ExpDate { get; set; }
    public int LevelRequirement { get; set; }
    public int AuctionCategoryA { get; set; }
    public int AuctionCategoryB { get; set; }
    public int AuctionCategoryC { get; set; }
    public int LevelLimit { get; set; }
    public int FixedGrade { get; set; }
    /// <summary>Highest grade reachable by regrading (-1 = uncapped); from items.max_enchantable_grade.</summary>
    public int MaxEnchantableGrade { get; set; } = -1;
    public bool Disenchantable { get; set; }
    public int LivingPointPrice { get; set; }
    public byte CharGender { get; set; }
    /// <summary>
    /// Highest <c>enchant_scale_ratios</c> row this item can be tempered to (items.max_enchant_scale_id).
    /// 0 means the item is not temperable.
    /// </summary>
    public byte MaxEnchantScaleId { get; set; }

    /// <summary>
    /// Highest <c>item_grades.id</c> this item may be regraded to (items.max_enchantable_grade).
    /// -1, the value most items carry, means no ceiling beyond the top of the grade table.
    /// </summary>
    public int MaxEnchantableGrade { get; set; } = -1;
    public uint SpecialtyZoneId { get; set; }
    // Defaults to the house commission rate until the template is loaded from items.
    public AuctionSettings AuctionSettings { get; set; } = new(0, 0, 0, 0, true);

    // Helpers
    public string searchString { get; set; }

    /*, 0, true*/
}
