using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Skills.Plots
{
    public class PlotStep
    {
        public PlotEventTemplate Event { get; set; }
        public byte Flag { get; set; }
        public int Delay { get; set; }
        public int Speed { get; set; }
        public bool Casting { get; set; }
        public bool Channeling { get; set; }
        public LinkedList<PlotStep> Steps { get; set; }

        public PlotStep()
        {
            Steps = new LinkedList<PlotStep>();
        }
    }
}