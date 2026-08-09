namespace AAEmu.Game.Models.Game.TodayAssignment;

public class TodayQuestStepTemplate
{
    public uint Id { get; set; }
    public uint RealStep { get; set; }
    /// <summary>Optional bag cost to unlock this step (e.g. Blue Salt Hammer 8329).</summary>
    public uint ItemId { get; set; }
    public int ItemNum { get; set; }
    public bool OrUnitReqs { get; set; }
    public int LevelMin { get; set; }
    public int LevelMax { get; set; }
    public List<TodayQuestGroupTemplate> Groups { get; } = [];
}
