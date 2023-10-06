using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncClimateReact : DoodadPhaseFuncTemplate
{
    public int NextPhase { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncClimateReact");

        var inMatchingClimate = ZoneManager.DoodadHasMatchingClimate(owner);

        // If no match, just move on to the next check
        if (!inMatchingClimate || NextPhase <= 0)
            return false;

        // override the next phase, and jump to it
        owner.OverridePhase = NextPhase;
        return true;
    }
}
