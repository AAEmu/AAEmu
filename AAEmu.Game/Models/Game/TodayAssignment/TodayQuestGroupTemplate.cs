namespace AAEmu.Game.Models.Game.TodayAssignment;

public class TodayQuestGroupTemplate
{
    public uint Id { get; set; }
    public uint StepId { get; set; }
    public bool OrUnitReqs { get; set; }
    public bool AutomaticRestart { get; set; }
    public List<uint> QuestContextIds { get; } = [];
}
