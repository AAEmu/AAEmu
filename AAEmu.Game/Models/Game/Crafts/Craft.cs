using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Crafts;

/*
    Data relating to a craft.
*/
public class Craft
{
    public uint Id { get; set; }
    public int CastDelay { get; set; }
    // 10.0.2.13: ToolId removed
    public uint SkillId { get; set; }
    public uint WiId { get; set; }
    public uint MilestoneId { get; set; }
    public uint ReqDoodadId { get; set; }
    // 10.0.2.13: NeedBind, AcId removed
    public int ActabilityLimit { get; set; }
    // 10.0.2.13: ShowUpperCraft removed
    public int RecommendLevel { get; set; }
    public int VisibleOrder { get; set; }

    /// <summary>Craft orders may name this craft as the work they want done.</summary>
    public bool Orderable { get; set; }

    /// <summary>Money the craft order board charges for this craft, in copper.</summary>
    public int Cost { get; set; }

    /// <summary>
    /// Actability group this craft belongs to, taken from its skill. Zero when the craft's skill is
    /// missing or names no group; the board's actability filter skips such crafts.
    /// </summary>
    public uint ActabilityGroupId { get; set; }

    /// <summary>Whether the shipped craft row is restricted to actability use.</summary>
    public bool UseOnlyActability { get; set; }

    /// <summary>
    /// Optional product-pack reference stored on the craft row. The current 10.0.2.13 compact
    /// catalog has no product-pack table, so consumers must not infer a join from this value.
    /// </summary>
    public uint? ProductPackId { get; set; }

    public uint? CraftCCategoryId { get; set; }
    public uint? CraftDCategoryId { get; set; }

    public List<CraftProduct> CraftProducts { get; set; } = [];
    public List<CraftMaterial> CraftMaterials { get; set; } = [];
    public List<CraftLineComponent> CraftLineComponents { get; set; } = [];
    public HashSet<uint> CraftPackIds { get; set; } = [];
    public bool IsPack { get; set; }

    public bool ResultsInBackpack
    {
        get
        {
            return CraftProducts.Select(product => ItemManager.Instance.GetTemplate(product.ItemId))
                .OfType<BackpackTemplate>().Any();
        }
    }
}
