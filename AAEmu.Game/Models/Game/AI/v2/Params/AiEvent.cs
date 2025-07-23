namespace AAEmu.Game.Models.Game.AI.v2.Params;

public class AiEvent
{
    public int Id { get; set; }
    public int IgnoreCategoryId { get; set; }
    public float Weight { get; set; }      // было ignore_time → теперь вес!
    public string EventName { get; set; }
    public int NpcId { get; set; }
    public bool OrUnitReqs { get; set; }
    public int SkillId { get; set; }
}
