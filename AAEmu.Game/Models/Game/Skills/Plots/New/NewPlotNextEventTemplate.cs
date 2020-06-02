using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Plots.New
{
    public class NewPlotNextEventTemplate
    {
        public NewPlotEventTemplate Event { get; set; }
        public int Position { get; set; }
        
        public NewPlotEventTemplate NextEvent { get; set; }
        
        public bool PerTarget { get; set; }
        public bool Casting { get; set; }
        public int Delay { get; set; }
        public int Speed { get; set; }
        public bool Channeling { get; set; }
        public int CastingInc { get; set; } // 10, 11, 14, 40, 500
        public bool AddAnimCsTime { get; set; }
        public bool CastingDelayable { get; set; }
        public bool CastingCancelable { get; set; }
        public bool CancelOnBigHit { get; set; }
        public bool UseExeTime { get; set; }
        public bool Fail { get; set; }

        public void Execute(PlotCaster caster, PlotTarget target, ushort tlId, Skill skill)
        {
            // Get total delay to apply
            var totalDelay = Delay;

            if (Speed > 0)
            {
                var dist = MathUtil.CalculateDistance(caster.OriginalCaster.Position, target.OriginalTarget.Position);
                totalDelay += (int)(dist / Speed * 1000f);
            }

            // Create a task that will execute the NextEvent
            if (totalDelay > 0)
            {
                // Create the task
                var task = new PlotEventTask()
                {
                    Caster = caster,
                    Target = target,
                    TlId = tlId,
                    EventTemplate = NextEvent,
                    Skill = skill
                };
                
                TaskManager.Instance.Schedule(task, TimeSpan.FromMilliseconds(totalDelay));
            }
            // Or execute it directly
            else
            {
                NextEvent.Execute(caster, target, tlId, skill);
            }
        }
    }
}
