namespace AAEmu.Game.Models.Game.AI.v2.Params;

public class AiEvent
{
    public int Id { get; set; }
    public int IgnoreCategoryId { get; set; }
    public float Weight { get; set; }      // was ignore_time → now it's weight!
    public string EventName { get; set; }
    public int NpcId { get; set; }
    public bool OrUnitReqs { get; set; }
    public int SkillId { get; set; }
}
