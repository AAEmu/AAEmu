using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncGrowth : DoodadFuncTemplate
    {
        public int Delay { get; set; }
        public int StartScale { get; set; }
        public int EndScale { get; set; }
        public int NextPhase { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncGrowth: skillId {0}, nextPhase {1},  Delay {2}, StartScale {3}, EndScale {4}, NextPhase {5}",
                skillId, nextPhase, Delay, StartScale, EndScale, NextPhase);

            //TODO add doodad scaling transformation
            owner.Scale = StartScale / 1000f;
            var customDelay = Delay; // / 100.0f; // decrease delay

            //if (owner.FuncTask != null)
            //{
            //    _ = owner.FuncTask.Cancel();
            //    _ = owner.FuncTask = null;
            //    _log.Trace("DoodadFuncGrowthTask: The current timer has been canceled by the next scheduled timer.");
            //}
            owner.FuncTask = new DoodadFuncGrowthTask(caster, owner, skillId, NextPhase, EndScale / 1000f);
            owner.GrowthTime = DateTime.UtcNow.AddMilliseconds(customDelay);
            TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(customDelay));
        }
    }
}
