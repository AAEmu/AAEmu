using AAEmu.Game.Models.Game.Skills.Plots.Type;

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

        public bool Execute()
        {

            return false;
        }
    }
}
