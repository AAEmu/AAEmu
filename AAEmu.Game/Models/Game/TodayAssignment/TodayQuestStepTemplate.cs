namespace AAEmu.Game.Models.Game.TodayAssignment;

public class TodayQuestStepTemplate
{
    public uint Id { get; set; }
    public uint RealStep { get; set; }
    /// <summary>Optional bag cost to unlock this step (from today_quest_steps.item_id / item_num).</summary>
    public uint ItemId { get; set; }
    public int ItemNum { get; set; }
    public bool OrUnitReqs { get; set; }

    /// <summary>
    /// enum_today_quest_sorts row - which board this step belongs to. 4 is the hero board.
    /// </summary>
    /// <remarks>
    /// Needed because the board decides what LevelMin and LevelMax mean; see TodayAssignmentManager.
    /// </remarks>
    public int SortId { get; set; }

    /// <summary>
    /// Low end of the eligibility range - a character level on most boards, a HERO GRADE on the hero one.
    /// </summary>
    public int LevelMin { get; set; }

    /// <summary>High end of the same.</summary>
    public int LevelMax { get; set; }
    public List<TodayQuestGroupTemplate> Groups { get; } = [];
}
