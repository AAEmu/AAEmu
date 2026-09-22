using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>Server-side representation of the ID-only siege raid descriptor.</summary>
public sealed class DoodadFuncSiegeRaid : DoodadFuncTemplate
{
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        // The descriptor itself does not change doodad state; registration uses the siege raid packets.
    }
}
