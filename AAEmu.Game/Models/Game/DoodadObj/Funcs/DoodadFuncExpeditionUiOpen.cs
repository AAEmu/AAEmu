using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public sealed class DoodadFuncExpeditionUiOpen : DoodadFuncTemplate
{
    public bool Creation { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is Character character && !Creation)
            ExpeditionManager.SendExpeditionInfo(character);
    }
}
