using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots.New
{
    public class NewPlot
    {
        public uint Id { get; set; }
        public SkillTargetType TargetTypeId { get; set; }

        public NewPlotEventTemplate FirstEvent { get; set; }

        public void Execute(Unit caster, SkillCaster skillCaster, Unit target, SkillCastTarget skillCastTarget, SkillObject skillObject)
        {
            var plotCaster = new PlotCaster()
            {
                OriginalCaster = caster,
                PreviousCaster = caster
            };
            
            var plotTarget = new PlotTarget()
            {
                PreviousTarget = target,
                OriginalTarget = target
            };
            
            // Run first event
            FirstEvent.Execute(plotCaster, plotTarget);
        }
    }
}
