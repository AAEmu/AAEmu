using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>Server-side representation of the ID-only item-changer UI descriptor.</summary>
public sealed class DoodadFuncItemChangerUiOpen : DoodadFuncTemplate
{
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        // The descriptor itself does not change doodad state; CSDoodadItemChangerPacket applies a selection.
    }
}
