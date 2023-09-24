using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncTimer : DoodadPhaseFuncTemplate
{
    public int Delay { get; set; }
    public int NextPhase { get; set; }
    public bool KeepRequester { get; set; }
    public bool ShowTip { get; set; }
    public bool ShowEndTime { get; set; }
    public string Tip { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        if (NextPhase > 0)
        {
            if (caster is Character)
                _log.Debug("DoodadFuncTimer: TemplateId {0},  Delay {1}, NextPhase {2}, KeepRequester {3}, ShowTip {4}, ShowEndTime {5}, Tip {6}", owner.TemplateId, Delay, NextPhase, KeepRequester, ShowTip, ShowEndTime, Tip);
            else
                _log.Trace("DoodadFuncTimer: TemplateId {0},  Delay {1}, NextPhase {2}, KeepRequester {3}, ShowTip {4}, ShowEndTime {5}, Tip {6}", owner.TemplateId, Delay, NextPhase, KeepRequester, ShowTip, ShowEndTime, Tip);

            double customDelay = Delay;
            var timeLeft = customDelay;

            if (owner.OverridePhaseTime > DateTime.MinValue)
            {
                // Reset the override
                owner.PhaseTime = owner.OverridePhaseTime;
                owner.OverridePhaseTime = DateTime.MinValue;

                var timeSincePhaseStart = DateTime.UtcNow - owner.PhaseTime;
                timeLeft = customDelay - timeSincePhaseStart.TotalMilliseconds;
            }

            if (timeLeft < 1)
                timeLeft = 1;

            // Not needed anymore?
            if (owner.OverrideGrowthTime > DateTime.MinValue)
            {
                // Reset the override
                owner.GrowthTime = owner.OverrideGrowthTime;
                owner.OverrideGrowthTime = DateTime.MinValue;
            }
            else
            {
                // owner.GrowthTime = DateTime.UtcNow.AddMilliseconds(timeLeft);
            }
            owner.GrowthTime = DateTime.UtcNow.AddMilliseconds(timeLeft);

            owner.FuncTask = new DoodadFuncTimerTask(caster, owner, 0, NextPhase);
            TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(timeLeft));
        }

        // никогда не прерываем последовательность фазовых функций
        // we never interrupt the sequence of phase functions
        return false;
    }
}
