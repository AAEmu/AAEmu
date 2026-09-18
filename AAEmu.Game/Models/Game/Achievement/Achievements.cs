using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Achievement;

public partial class Achievements
{
    public Achievements()
    {
        AchievementObjectives = new HashSet<AchievementObjectives>();
    }

    public uint Id { get; set; }
    public uint CompleteNum { get; set; }
    public bool CompleteOr { get; set; }
    public string Description { get; set; }
    //public uint GradeId { get; set; }
    public uint IconId { get; set; }
    public bool IsHidden { get; set; }
    public uint ItemNum { get; set; }
    public uint ItemId { get; set; }
    public string Name { get; set; }
    public bool OrUnitReqs { get; set; }
    public uint ParentAchievementId { get; set; }
    public uint Priority { get; set; }
    public uint SubCategoryId { get; set; }
    public string Summary { get; set; }

    /// <summary>The title this achievement pays, when it pays one (214 of them do).</summary>
    public uint AppellationId { get; set; }

    /// <summary>
    /// The client's own reward tier for this achievement (644 carry one). The window draws it next to the
    /// reward icons; nothing server-side reads it.
    /// </summary>
    public uint GradeId { get; set; }

    /// <summary>
    /// Whether the season has switched this achievement off (967 rows). It cannot be earned while it is off,
    /// so it is not something its parent or its sub-category waits for.
    /// </summary>
    public bool SeasonOff { get; set; }

    //public virtual Icons Icon { get; set; }
    public virtual ItemTemplate Item { get; set; }
    public virtual ICollection<AchievementObjectives> AchievementObjectives { get; set; }
}
