using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots.New;

namespace AAEmu.Game.Models.Tasks.Skills
{
    public class PlotEventTask : Task
    {
        public PlotCaster Caster { get; set; } 
        public PlotTarget Target { get; set; }
        public ushort TlId { get; set; }
        public NewPlotEventTemplate EventTemplate { get; set; }
        public Skill Skill { get; set; }
        
        public override void Execute()
        {
            EventTemplate.Execute(Caster, Target, TlId, Skill);
        }
    }
}
