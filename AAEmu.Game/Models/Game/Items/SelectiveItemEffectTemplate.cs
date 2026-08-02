namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Row from <c>selective_item_effects</c> — skill-driven selection chest UI.
/// Client completes the pick with <c>CSInvokeItemSelectiveItemEffect (0x1BB)</c>.
/// </summary>
public class SelectiveItemEffectTemplate
{
    public uint Id { get; set; }
    public uint SkillId { get; set; }
    /// <summary>How many options the player must/may pick (UI).</summary>
    public int SelectCount { get; set; }
    /// <summary>How many of the source chest item to burn on confirm.</summary>
    public int ConsumeItemCount { get; set; }
    /// <summary>When true, UI allows multi-select up to <see cref="SelectCount"/>.</summary>
    public bool IsMulti { get; set; }
    public List<SelectiveItemEffectElem> Elems { get; set; } = [];
}

public class SelectiveItemEffectElem
{
    public uint Id { get; set; }
    public uint SelectiveItemEffectId { get; set; }
    public uint ItemId { get; set; }
    public byte GradeId { get; set; }
    public int Count { get; set; }
}
