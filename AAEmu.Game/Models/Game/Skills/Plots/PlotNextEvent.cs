namespace AAEmu.Game.Models.Game.Skills.Plots
{
    public class PlotNextEvent
    {
        public PlotEventTemplate Event { get; set; }
        public int Position { get; set; }
        public bool PerTarget { get; set; }
        public bool Casting { get; set; }
        public int Delay { get; set; }
        public int Speed { get; set; }
        public bool Channeling { get; set; }
        public int CastingInc { get; set; }
        public bool AddAnimCsTime { get; set; }
        public bool CastingDelayable { get; set; }
        public bool CastingCancelable { get; set; }
        public bool CancelOnBigHit { get; set; }
        public bool UseExeTime { get; set; }
        public bool Fail { get; set; }
    }
}