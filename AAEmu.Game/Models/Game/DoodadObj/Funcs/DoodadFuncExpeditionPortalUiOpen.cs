using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public sealed class DoodadFuncExpeditionPortalUiOpen : DoodadFuncTemplate
{
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is Character character)
            ExpeditionActivityServices.Get().SendPortals(character);
    }
}
