using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class EffectSource
    {
        public Skill Skill { get; set; }
        public BuffTemplate Buff { get; set; }

        public EffectSource()
        {
        }
        
        public EffectSource(Skill skill)
        {
            Skill = skill;
        }

        public EffectSource(BuffTemplate buff)
        {
            Buff = buff;
        }

        public EffectSource(Skill skill, BuffTemplate buff)
        {
            Skill = skill;
            Buff = buff;
        }
    }
}
