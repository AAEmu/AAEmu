using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Heirs;

/// <summary>
/// Row of <c>heir_skill_details</c>: one selectable successor for a base <see cref="HeirSkill"/>.
/// </summary>
public class HeirSkillDetail
{
    public uint Id { get; set; }
    public uint HeirSkillId { get; set; }
    public uint SkillId { get; set; }
    public int Pos { get; set; }
    public SkillActiveType SkillActiveTypeId { get; set; }
    public string Desc { get; set; }
    public uint ActiveItemId { get; set; }
}
