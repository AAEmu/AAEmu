using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Plots.New
{
    public class NewPlotNextEventTemplate
    {
        public NewPlotEventTemplate Event { get; set; }
        public uint Position { get; set; }
        
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

        public void Execute(PlotCaster caster, SkillCaster skillCaster, PlotTarget target, SkillCastTarget skillCastTarget, SkillObject skillObject, ushort tlId, Skill skill, Dictionary<uint, int> callCounter)
        {
            // Get total delay to apply
            var totalDelay = 0;
            if (Delay > 0)
            {
                totalDelay = Delay;
            }

            if (Speed > 0)
            {
                var dist = MathUtil.CalculateDistance(caster.OriginalCaster.Position, target.OriginalTarget.Position);
                totalDelay += (int)(dist / Speed * 1000f);
            }

            // Console.WriteLine("Plot next event! Event: {0}, NextEvent: {1}, Delay: {2}", this.Event.Id, this.NextEvent.Id, totalDelay);
            
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
                    Skill = skill,
                    SkillCaster = skillCaster,
                    SkillCastTarget =  skillCastTarget,
                    SkillObject = skillObject,
                    Counter = callCounter
                };
                
                TaskManager.Instance.Schedule(task, TimeSpan.FromMilliseconds(totalDelay));
            }
            // Or execute it directly
            else
            {
                NextEvent.Execute(caster, skillCaster, target, skillCastTarget, skillObject, tlId, skill, callCounter);
            }
        }
    }
}
