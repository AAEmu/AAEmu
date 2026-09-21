using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// The craft order board's F-key interaction. The client opens the board window itself; the server's
/// part is to hand it the orders this character has posted, which is what the "my orders" tab reads.
/// </summary>
public sealed class DoodadFuncCraftOrderBoardUiOpen : DoodadFuncTemplate
{
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is Character character)
            CraftOrderManager.Instance.SendOwnEntries(character);
    }
}
