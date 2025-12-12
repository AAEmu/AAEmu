using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Tasks.Skills;

public abstract class SkillTask(Skill skill) : Task
{
    public Skill Skill { get; set; } = skill;
}