using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncGrowth : DoodadPhaseFuncTemplate
    {
        public int Delay { get; set; }
        public int StartScale { get; set; }
        public int EndScale { get; set; }
        public int NextPhase { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            //TODO add doodad scaling transformation
            owner.Scale = StartScale / 1000f;
            var customDelay = Delay; // / 100.0f; // decrease delay

            //if (owner.FuncTask != null)
            //{
            //    _ = owner.FuncTask.Cancel();
            //    _ = owner.FuncTask = null;
            //    _log.Debug("DoodadFuncGrowthTask: The current timer has been canceled by the next scheduled timer.");
            //}
            _log.Debug("DoodadFuncGrowth: Delay {0}, StartScale {1}, EndScale {2}, NextPhase {3}", Delay, StartScale, EndScale, NextPhase);
            owner.FuncTask = new DoodadFuncGrowthTask(caster, owner, 0, NextPhase, EndScale);
            owner.GrowthTime = DateTime.UtcNow.AddMilliseconds(customDelay);
            TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(customDelay));
            return false;
        }
    }
}
