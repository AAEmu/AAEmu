namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// One awakening recipe (<c>item_change_mappings</c>): which item, at which grade, turns into which
/// other item.
/// </summary>
public class ItemChangeMapping
{
    public uint Id { get; set; }
    public uint MappingGroupId { get; set; }
    public uint SourceItemId { get; set; }
    public uint TargetItemId { get; set; }

    /// <summary>Grade the source has to be at, or -1 when any grade qualifies.</summary>
    public int SourceGradeId { get; set; }

    /// <summary>Grade the result is forced to, or -1 to carry the source's grade over.</summary>
    public int TargetGradeId { get; set; }
}

/// <summary>
/// The odds behind a group of awakening recipes (<c>item_change_mapping_groups</c>). The awakening
/// scroll's skill names the group through its special effect's first value.
/// </summary>
/// <remarks>
/// <see cref="Success"/>, <see cref="Disable"/> and <see cref="FailBonus"/> are per 10000. A group
/// with <see cref="Selectable"/> lets the player pick which of the group's candidate results to aim
/// for; without it the server picks one at random and the client hides the radio buttons.
/// </remarks>
public class ItemChangeMappingGroup
{
    public uint Id { get; set; }
    public string Name { get; set; }

    /// <summary>Chance the awakening succeeds.</summary>
    public int Success { get; set; }

    /// <summary>Chance a failure additionally locks the item ("disabled") instead of just failing.</summary>
    public int Disable { get; set; }

    /// <summary>Added to the success chance for every failure the item has already accumulated.</summary>
    public int FailBonus { get; set; }

    public bool Selectable { get; set; }

    /// <summary>Whether synthesis progress carries over to the awakened item.</summary>
    public bool EvolvingExpInherit { get; set; }

    public List<ItemChangeMapping> Mappings { get; set; } = [];
}
