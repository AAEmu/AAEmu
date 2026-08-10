namespace AAEmu.Game.Models.Game.TodayAssignment;

public class TodayQuestStepTemplate
{
    public uint Id { get; set; }
    public uint RealStep { get; set; }
    /// <summary>Optional bag cost to unlock this step (from today_quest_steps.item_id / item_num).</summary>
    public uint ItemId { get; set; }
    public int ItemNum { get; set; }
    public bool OrUnitReqs { get; set; }
    public int LevelMin { get; set; }
    public int LevelMax { get; set; }
    public List<TodayQuestGroupTemplate> Groups { get; } = [];
}
