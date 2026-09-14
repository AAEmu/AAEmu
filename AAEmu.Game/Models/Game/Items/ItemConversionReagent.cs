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
    /// PostLoad. 5496 of the 5742 packs in <c>item_conv_rpack_members</c> feed one conversion and 246 feed
    /// two.
    /// </summary>
    public List<uint> ConversionIds = [];

    /// <summary>
    /// The <c>item_conv_sets</c> families those conversions belong to, minus the ones the content leaves
    /// NULL. 114 of the 5618 packs that <c>item_conv_reagents</c> and <c>item_conv_reagent_filters</c>
    /// reference feed more than one family, and reagent pack 2062 is one: its family-11 conversion 5740 pays
    /// 18 sealed Ipnir enhancers while its family-4 "dummy" conversion 2060 pays 34 of an unrelated item. The
    /// family the effect asks for decides which of them may pay out.
    /// </summary>
    public HashSet<uint> ConversionFamilies = [];

    /// <summary>
    /// Conversions of this pack whose <c>item_conv_set_id</c> is NULL, so no family can attribute them.
    /// 11 referenced packs mix one of those with a known family and the NULL route is the real one: reagent
    /// pack 2725 holds the family-4 "dummy" (145 of item 46185) next to
    /// <c>discontinued_ship_paper.common</c> (1 of item 46831), covering 3 items in all. Seven more have
    /// nothing but unattributed routes, the origin-land armour socket disenchants among them, covering 65
    /// items.
    /// </summary>
    public List<uint> UnattributedConversionIds = [];

    /// <summary>True for an explicit <c>item_conv_reagents</c> row, false for a filter row.</summary>
    public bool IsExplicitItem;

    /// <summary>
    /// False when none of the pack's conversions names a family. Reported at load so the content gap is
    /// visible; whether a cast may proceed is decided per request by the loader's route selection.
    /// </summary>
    public bool HasKnownFamily => ConversionFamilies.Count > 0;

    public bool HasFamily(uint conversionSetId) => ConversionFamilies.Contains(conversionSetId);

    public bool MatchesGrade(byte grade) => grade >= MinItemGrade && grade <= MaxItemGrade;
}
