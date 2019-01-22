using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Tasks.Skills
{
    public abstract class SkillTask : Task
    {
        public Skill Skill { get; set; }

        protected SkillTask(Skill skill)
        {
            Skill = skill;
        }
    }
}