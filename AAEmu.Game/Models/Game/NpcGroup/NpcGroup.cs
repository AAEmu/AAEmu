namespace AAEmu.Game.Models.Game.NpcGroup;

public class NpcGroup
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int AggroRuleId { get; set; }
    public bool EnableRespawn { get; set; }
}
