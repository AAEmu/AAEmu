using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// One reagent side of an item conversion (<c>item_conv_reagents</c> / <c>item_conv_reagent_filters</c>).
/// </summary>
public class ItemConversionReagent
{
    /// <summary>
    /// <c>item_conv_sets.id</c> reached through the conversion this reagent pack belongs to. This is the
    /// value the ItemConversion special effect carries in its first value, so it is what validates that a
    /// client asked for the conversion family the item actually supports.
    /// </summary>
    public uint ConversionSet;

    /// <summary><c>item_conv_rpacks.id</c> this reagent row belongs to.</summary>
    public uint ReagentPackId;

    public ItemImplEnum ImplId;
    public uint InputItemId;
    public int MinLevel;
    public int MaxLevel;
    public byte MinItemGrade;
    public byte MaxItemGrade;

    /// <summary>
    /// <c>item_conv_reagent_filters.item_conv_epack_id</c>. Non-zero on the five "01-19" armour filters
    /// whose <c>item_conv_epacks</c> row excludes an item category, so the filter does not swallow items
    /// the client keeps out of the pack.
    /// </summary>
    public uint ExceptionPackId;

    /// <summary>
    /// Conversions (<c>item_conv_rpack_members.item_conv_id</c>) this reagent pack feeds, resolved in
    /// PostLoad. 122 packs feed two conversions, the rest feed one.
    /// </summary>
    public List<uint> ConversionIds = [];

    /// <summary>True for an explicit <c>item_conv_reagents</c> row, false for a filter row.</summary>
    public bool IsExplicitItem;

    public bool MatchesGrade(byte grade) => grade >= MinItemGrade && grade <= MaxItemGrade;
}
