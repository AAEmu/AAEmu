namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// What a regrade attempt rolls at one item grade (<c>item_enchant_ratios</c>). Ratios are per
/// 10000, the same scale tempering uses.
/// </summary>
/// <remarks>
/// <para>
/// 10.0.2.13 moved these out of <c>item_grades</c>, where AAEmu had always read them, and made them
/// per item rather than global: a row is keyed by grade <em>and</em>
/// <see cref="GroupId"/>, so the same grade can be cheap and safe on crafted gear and brutal on a
/// world drop. <see cref="ItemEnchantRatioGroup"/> is what decides which set an item gets.
/// </para>
/// <para>
/// <see cref="Grade"/> is an <c>item_grades.id</c>, not a <c>grade_order</c>. The two disagree at
/// the bottom of the table - id 1 is Lv.0 (order 0) and id 0 is Lv.1 (order 1) - and the shipped
/// costs confirm the id reading: the row for grade 1 costs 1, the row for grade 0 costs 9, and
/// everything above rises monotonically. Match rows on <c>Item.Grade</c>; only stepping a grade up
/// or down goes through <c>grade_order</c>.
/// </para>
/// </remarks>
public class ItemEnchantRatio
{
    public uint Id { get; set; }

    /// <summary><see cref="ItemEnchantRatioGroup.Id"/> this row belongs to.</summary>
    public uint GroupId { get; set; }

    /// <summary>The <c>item_grades.id</c> the item must be sitting at for this row to apply.</summary>
    public int Grade { get; set; }

    public int SuccessRatio { get; set; }
    public int GreatSuccessRatio { get; set; }
    public int BreakRatio { get; set; }

    /// <summary>
    /// Chance a failure locks the item out of further enchanting until a restore item clears it.
    /// New in 10.0.2.13; the 1.2 schema had no such column.
    /// </summary>
    public int DisableRatio { get; set; }

    public int DowngradeRatio { get; set; }

    /// <summary>
    /// Lowest / highest <c>item_grades.id</c> a downgrade may drop to, both inclusive. -1 on both
    /// means the grade has no downgrade defined at all, which is how every low grade ships.
    /// </summary>
    public int DowngradeMin { get; set; }

    public int DowngradeMax { get; set; }

    /// <summary>Feeds <c>item_grade</c> in the GradeEnchantCost formula - a factor, not a price.</summary>
    public int Cost { get; set; }

    /// <summary>Wire currency the fee is charged in. 0 (coin) everywhere in shipped data.</summary>
    public uint CurrencyId { get; set; }

    /// <summary>Whether this grade can be enchanted upward at all.</summary>
    public bool IsTerminal => SuccessRatio <= 0;
}

/// <summary>
/// How <c>item_enchant_ratio_groups</c> says a group finds its items
/// (<c>enum_item_enchant_ratio_kinds</c>).
/// </summary>
public enum ItemEnchantRatioKind
{
    /// <summary>The fallback for everything no other group claims.</summary>
    Default = 1,

    /// <summary>Everything whose <c>items.impl_id</c> matches the group's.</summary>
    ItemImpl = 2,

    /// <summary>An explicit item list in <c>item_enchant_ratio_items</c>.</summary>
    Custom = 3
}

/// <summary>Which items a set of <see cref="ItemEnchantRatio"/> rows applies to.</summary>
public class ItemEnchantRatioGroup
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public ItemEnchantRatioKind Kind { get; set; }

    /// <summary>Only meaningful for <see cref="ItemEnchantRatioKind.ItemImpl"/>.</summary>
    public uint ItemImplId { get; set; }
}
