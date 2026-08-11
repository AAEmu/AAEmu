using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncPulseTrigger : DoodadPhaseFuncTemplate
{
    public bool Flag { get; set; }
    public int NextPhase { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        // Grab the calling PhaseFunc (shared phase-func row; Pulse resets PulseTriggered before re-entry).
        var thisPhaseFunc = owner.CurrentPhaseFuncs.FirstOrDefault(x => x.FuncId == Id);
        if (thisPhaseFunc == null)
        {
            Logger.Warn($"DoodadFuncPulseTrigger Flag={Flag}, NextPhase={NextPhase} was not triggered from a DoodadFuncPulseTrigger");
            return false;
        }

        // Flag=false triggers are discharge/reset markers; they do not advance.
        // Flag=true advances only after an external DoodadFuncPulse cleared PulseTriggered.
        // Non-character casters (boot settle, pure ToD) must not auto-charge: return false so
        // remaining phase funcs (ToD, etc.) still run. Player-driven pulses pass Character.
        if (!Flag || thisPhaseFunc.PulseTriggered)
            return false;

        if (caster is not Character)
            return false;

        Logger.Debug(
            "DoodadFuncPulseTrigger Flag={0}, NextPhase={1}, ownerTpl={2}",
            Flag, NextPhase, owner.TemplateId);

        thisPhaseFunc.PulseTriggered = true; // Prevent loops until next Pulse clears it
        owner.OverridePhase = NextPhase;
        return true;
    }
}
