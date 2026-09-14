using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// One reagent side of an item conversion (<c>item_conv_reagents</c> / <c>item_conv_reagent_filters</c>).
/// </summary>
public class ItemConversionReagent
{
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

    /// <summary>
    /// The <c>item_conv_sets</c> families those conversions belong to, minus the ones the content leaves
    /// NULL. 125 packs feed conversions in more than one family, and reagent pack 2062 is one: its family-11
    /// conversion 5740 pays 18 sealed Ipnir enhancers while its family-4 "dummy" conversion 2060 pays 34 of
    /// an unrelated item. The family the effect asks for decides which of them may pay out.
    /// </summary>
    public HashSet<uint> ConversionFamilies = [];

    /// <summary>True for an explicit <c>item_conv_reagents</c> row, false for a filter row.</summary>
    public bool IsExplicitItem;

    /// <summary>
    /// False when none of the pack's conversions names a family - 10 packs, among them the origin-land
    /// armour socket disenchants (rpack 331-335, 315 items), whose conversions carry a NULL
    /// <c>item_conv_set_id</c>. Nothing can be validated against those, so the cast is allowed.
    /// </summary>
    public bool HasKnownFamily => ConversionFamilies.Count > 0;

    public bool HasFamily(uint conversionSetId) => ConversionFamilies.Contains(conversionSetId);

    public bool MatchesGrade(byte grade) => grade >= MinItemGrade && grade <= MaxItemGrade;
}
