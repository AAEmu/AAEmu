using System;
using System.Security.Policy;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncGrowth : DoodadPhaseFuncTemplate
{
    public int Delay { get; set; }
    public int StartScale { get; set; }
    public int EndScale { get; set; }
    public int NextPhase { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        // TODO: Add doodad scaling transformation
        owner.Scale = StartScale / 1000f;
        var customDelay = Delay / AppConfiguration.Instance.World.GrowthRate; // decrease delay
        if (ZoneManager.Instance.DoodadHasMatchingClimate(owner))
            customDelay = (double)customDelay * 0.73f;
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

        owner.GrowthTime = DateTime.UtcNow.AddMilliseconds(timeLeft);

        if (caster is Character)
            Logger.Debug("DoodadFuncGrowth: Delay {0}, StartScale {1}, EndScale {2}, NextPhase {3}", Delay, StartScale, EndScale, NextPhase);
        else
            Logger.Trace("DoodadFuncGrowth: Delay {0}, StartScale {1}, EndScale {2}, NextPhase {3}", Delay, StartScale, EndScale, NextPhase);

        owner.FuncTask = new DoodadFuncGrowthTask(caster, owner, 0, NextPhase, EndScale / 1000f);
        TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(timeLeft));

        return false;
    }
}
