using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
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
        var startScale = DoodadGrowthScaleRules.ResolveScale(StartScale, EndScale, owner.Scale);
        var endScale = DoodadGrowthScaleRules.ResolveScale(EndScale, StartScale, startScale);
        owner.SetScale(startScale);
        var customDelay = Delay / AppConfiguration.Instance.World.GrowthRate; // decrease delay
        if (ZoneManager.Instance.DoodadHasMatchingClimate(owner))
            customDelay = customDelay * 0.73f;
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

        // Cancel the current task if it exists.
        if (owner.FuncTask != null)
        {
            try
            {
                TaskManager.Instance.Cancel(owner.FuncTask);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to cancel existing FuncTask: {0}", ex.Message);
            }
        }

        // Create and assign the completion task.
        owner.FuncTask = new DoodadFuncGrowthTask(caster, owner, 0, NextPhase, endScale);
        TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(timeLeft));

        // Guild worldgate active phase: persist growth across World bounce and push the HUD timer.
        var portalFuncs = DoodadManager.Instance.GetFuncsForGroup(owner.FuncGroupId)
            .Select(f => f.FuncType);
        if (ExpeditionPortalDoodadRules.PhaseOpensPortalUi(portalFuncs))
            ExpeditionActivityServices.Get().OnWorldgateTimerArmed(owner);

        return false;
    }
}
