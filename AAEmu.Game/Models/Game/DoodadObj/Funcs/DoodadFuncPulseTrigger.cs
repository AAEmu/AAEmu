using System.Linq;

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
        if (caster is not Character)
        {
            return true;
        }

        // Grab the calling PhaseFunc
        var thisPhaseFunc = owner.CurrentPhaseFuncs.FirstOrDefault(x => x.FuncId == Id);
        if (thisPhaseFunc == null)
        {
            Logger.Warn($"DoodadFuncPulseTrigger Flag={Flag}, NextPhase={NextPhase} was not triggered from a DoodadFuncPulseTrigger");
            return false; // Fail check as there seems to be a mismatch
        }

        Logger.Debug($"DoodadFuncPulseTrigger Flag={Flag}, NextPhase={NextPhase}, PulseTriggered={thisPhaseFunc.PulseTriggered}");

        if (Flag && !thisPhaseFunc.PulseTriggered)
        {
            thisPhaseFunc.PulseTriggered = true; // Prevent loops
            owner.OverridePhase = NextPhase;

            return true;
        }

        return false;

    }
}
