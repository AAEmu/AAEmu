namespace AAEmu.Game.Models.Game.Skills;

public class SkillReagent
{
    public uint Id;
    public uint SkillId;
    public uint ItemId;
    public int Amount;
    /// <summary><c>skill_reagents.enable</c>; see <see cref="SkillReagentLoadRules"/>.</summary>
    public bool Enable = true;
}
