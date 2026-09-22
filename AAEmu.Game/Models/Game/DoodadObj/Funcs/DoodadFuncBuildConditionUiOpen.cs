using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Identifies the construction-condition UI interaction. The client loads its display metadata
/// separately; material delivery and progress are handled by the accompanying <see cref="DoodadFuncDevote"/>.
/// </summary>
public sealed class DoodadFuncBuildConditionUiOpen : DoodadFuncTemplate
{
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        // This descriptor has no server-side fields or state transition.
    }
}
