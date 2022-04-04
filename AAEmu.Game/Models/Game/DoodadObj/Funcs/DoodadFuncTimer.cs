using System;
using System.Threading.Tasks;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

using Google.Protobuf.WellKnownTypes;

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
            if (NextPhase > 0)
            {
                //if (owner.FuncTask != null)
                //{
                //    _ = owner.FuncTask.Cancel();
                //    _ = owner.FuncTask = null;
                //    if (caster is Character)
                //        _log.Debug("DoodadFuncTimer: The current timer has been canceled by the next scheduled timer.");
                //    else
                //        _log.Trace("DoodadFuncTimer: The current timer has been canceled by the next scheduled timer.");
                //}
                if (caster is Character)
                    _log.Debug("DoodadFuncTimer: TemplateId {0},  Delay {1}, NextPhase {2}, KeepRequester {3}, ShowTip {4}, ShowEndTime {5}, Tip {6}", owner.TemplateId, Delay, NextPhase, KeepRequester, ShowTip, ShowEndTime, Tip);
                else
                    _log.Trace("DoodadFuncTimer: TemplateId {0},  Delay {1}, NextPhase {2}, KeepRequester {3}, ShowTip {4}, ShowEndTime {5}, Tip {6}", owner.TemplateId, Delay, NextPhase, KeepRequester, ShowTip, ShowEndTime, Tip);

                owner.FuncTask = new DoodadFuncTimerTask(caster, owner, 0, NextPhase);
                owner.GrowthTime = DateTime.UtcNow.AddMilliseconds(Delay);
                //TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(Delay));
                if (Delay > 0)
                {
                    // TODO : Add a proper delay in here
                    Task.Run(async () =>
                    {
                        await Task.Delay(Delay);
                        owner.FuncTask.Execute();
                    });
                }

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
