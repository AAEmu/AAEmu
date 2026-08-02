namespace AAEmu.Game.Models.Game.Heirs;

/// <summary>
/// Row of <c>heir_levels</c>: the cumulative exp a character needs to reach a heir level, and the
/// step that level belongs to. Steps are what <c>heir_skills</c> is keyed on.
/// </summary>
public class HeirLevel
{
    public uint Id { get; set; }
    public byte Level { get; set; }
    public long ReqTotalExp { get; set; }
    public byte Step { get; set; }
    public uint ReqItemId { get; set; }
    public int ReqItemCount { get; set; }
}

/// <summary>
/// Row of <c>heir_skills</c>: a base skill whose Heir successors become selectable at a step.
/// </summary>
public class HeirSkill
{
    public uint Id { get; set; }
    public uint SkillId { get; set; }
    public byte Step { get; set; }
    public bool Enable { get; set; }

    /// <summary>Successor choices owned by this content row, ordered by their client position.</summary>
    public IReadOnlyList<HeirSkillDetail> Successors { get; internal set; } = [];
}
