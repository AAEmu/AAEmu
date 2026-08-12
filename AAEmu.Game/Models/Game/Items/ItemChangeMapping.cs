namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// One awakening ("각성") route: which item, at which grade, becomes which other item.
/// </summary>
public class ItemChangeMapping
{
    public uint Id { get; set; }
    public uint MappingGroupId { get; set; }
    public uint SourceItemId { get; set; }
    public uint TargetItemId { get; set; }

    /// <summary>Grade the source must be at. -1 means any grade.</summary>
    public int SourceGradeId { get; set; }

    /// <summary>Grade the result is set to. -1 means keep the grade the source had.</summary>
    public int TargetGradeId { get; set; }
}

/// <summary>
/// The tuning shared by a set of awakening routes, named by an awakening scroll's
/// <c>item_change_mapping</c> special effect through its first value.
/// </summary>
public class ItemChangeMappingGroup
{
    public uint Id { get; set; }
    public string Name { get; set; }

    /// <summary>Base success chance in basis points; 10000 is guaranteed.</summary>
    public int Success { get; set; }

    /// <summary>
    /// Unused. Present in the data (and nonzero for the chance-based groups) but what it gates has
    /// not been established, so nothing is applied for it - guessing here could destroy player items.
    /// </summary>
    public int Disable { get; set; }

    /// <summary>
    /// Basis points added to the item's stored bonus on every failure, so repeated attempts on the
    /// same item get progressively likelier. Carried per item in <c>EquipItem.MappingFailBonus</c>.
    /// </summary>
    public int FailBonus { get; set; }

    /// <summary>True when a source can map to more than one target and the player picks.</summary>
    public bool Selectable { get; set; }

    /// <summary>True when the result keeps the synthesis EXP the source had accumulated.</summary>
    public bool EvolvingExpInherit { get; set; }

    public readonly List<ItemChangeMapping> Mappings = [];
}
