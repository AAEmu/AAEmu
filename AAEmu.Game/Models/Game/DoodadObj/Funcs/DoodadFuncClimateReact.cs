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
        _log.Trace("DoodadFuncClimateReact");

        var inMatchingClimate = ZoneManager.Instance.DoodadHasMatchingClimate(owner);

        // If no match, just move on to the next check
        if (!inMatchingClimate || NextPhase <= 0)
            return false;

        // override the next phase, and jump to it
        owner.OverridePhase = NextPhase;
        return true;
    }
}
