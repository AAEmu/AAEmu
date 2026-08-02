namespace AAEmu.Game.Models.Game.Skills.Templates;

public class PassiveBuffTemplate
{
    public uint Id { get; set; }
    public AbilityType AbilityId { get; set; }
    public byte Level { get; set; }
    public uint BuffId { get; set; }
    public int ReqPoints { get; set; }
    /// <summary>Cost against the shared skill-point pool (client loads this from passive_buffs.skill_points).</summary>
    public int SkillPoints { get; set; }
    public bool Active { get; set; }
}
