using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncTimer : DoodadFuncTemplate
    {
        public int Delay { get; set; }
        public int NextPhase { get; set; }
        public bool KeepRequester { get; set; }
        public bool ShowTip { get; set; }
        public bool ShowEndTime { get; set; }
        public string Tip { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncTimer: skillId {0}, nextPhase {1},  Delay {2}, NextPhase {3}, KeepRequester {4}, ShowTip {5}, ShowEndTime {6}, Tip {7}",
                skillId, nextPhase, Delay, NextPhase, KeepRequester, ShowTip, ShowEndTime, Tip);

            owner.GrowthTime = DateTime.UtcNow.AddMilliseconds(Delay + 1); // TODO need here

            if (NextPhase > 0)
            {
                if (owner.FuncTask != null)
                {
                    _ = owner.FuncTask.Cancel();
                    _ = owner.FuncTask = null;
                }
                owner.FuncTask = new DoodadFuncTimerTask(caster, owner, skillId, NextPhase);
                TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(Delay + 1));
            }
            else
            {
                //Wondering if more needs done here if depending on next phase func
                //owner.Use(caster, skillId);
            }
        }
    }
}
