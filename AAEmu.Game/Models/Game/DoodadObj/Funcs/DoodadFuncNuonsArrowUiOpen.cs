using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>Server-side representation of the ID-only Nuon's Arrow UI descriptor.</summary>
public sealed class DoodadFuncNuonsArrowUiOpen : DoodadFuncTemplate
{
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        // The descriptor itself does not change doodad state. CSFireNuonsArrowPacket is parsed separately.
    }
}
