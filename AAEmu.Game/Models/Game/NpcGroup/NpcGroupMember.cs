namespace AAEmu.Game.Models.Game.NpcGroup;

public class NpcGroupMember
{
    public int Id { get; set; }
    public int NpcGroupId { get; set; }
    public int NpcId { get; set; }
    public bool IsLeader { get; set; }
    public bool IsMoveLeader { get; set; }
    public float FormationOffsetX { get; set; }
    public float FormationOffsetY { get; set; }
    public float FormationOffsetZ { get; set; }
    public float FormationTension { get; set; }
}
