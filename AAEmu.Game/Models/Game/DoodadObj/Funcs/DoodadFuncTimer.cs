using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncTimer : DoodadPhaseFuncTemplate
    {
        public int Delay { get; set; }
        public int NextPhase { get; set; }
        public bool KeepRequester { get; set; }
        public bool ShowTip { get; set; }
        public bool ShowEndTime { get; set; }
        public string Tip { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            owner.GrowthTime = DateTime.UtcNow.AddMilliseconds(Delay + 1); // TODO need here

            if (NextPhase > 0)
            {
                //if (owner.FuncTask != null)
                //{
                //    _ = owner.FuncTask.Cancel();
                //    _ = owner.FuncTask = null;
                //    _log.Debug("DoodadFuncTimer: The current timer has been canceled by the next scheduled timer.");
                //}
                _log.Debug("DoodadFuncTimer: TemplateId {0},  Delay {1}, NextPhase {2}, KeepRequester {3}, ShowTip {4}, ShowEndTime {5}, Tip {6}", owner.TemplateId, Delay, NextPhase, KeepRequester, ShowTip, ShowEndTime, Tip);
                owner.FuncTask = new DoodadFuncTimerTask(caster, owner, 0, NextPhase);
                TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(Delay + 1));
            }
            else
            {
                //Wondering if more needs done here if depending on next phase func
                //owner.Use(caster, skillId);
            }

            return false; // никогда не прерываем последовательность фазовых функций
        }
    }
}
