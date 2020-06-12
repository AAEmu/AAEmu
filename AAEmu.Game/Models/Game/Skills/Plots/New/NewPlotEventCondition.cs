using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots.New
{
    public class NewPlotEventCondition
    {
        public NewPlotEventTemplate Event { get; set; }
        public NewPlotCondition Condition { get; set; }
        public uint Position { get; set; }
        public PlotEffectSource Source { get; set; }
        public PlotEffectSource Target { get; set; }
        public bool NotifyFailure { get; set; }

        public bool Execute(Unit caster, SkillCaster skillCaster, Unit target, SkillCastTarget skillCastTarget, SkillObject skillObject)
        {
            return Condition.Execute(caster, skillCaster, target, skillCastTarget, skillObject);
        }
    }
}
