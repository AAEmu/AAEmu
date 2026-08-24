using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncExitIndun : DoodadFuncTemplate
{
    // doodad_funcs
    public uint ReturnPointId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Info("DoodadFuncExitIndun, ReturnPointId: {0}", ReturnPointId);

        if (caster is Character character)
        {
            // ReturnPointId 0 = "leave to wherever we entered from". Leave paths fall back to the
            // character's return district when MainWorldPosition was never saved (older enter path).
            if (ReturnPointId == 0)
            {
                if (!IndunManager.Instance.RequestLeaveInstance(character))
                {
                    Logger.Info("DoodadFuncExitIndun, leave request failed for {0}", character.Name);
                    character.SendErrorMessage(ErrorMessageType.InvalidReturnPosInstance);
                }
            }
            else
            {
                Logger.Info("DoodadFuncExitIndun, Not have return point!");
                character.SendErrorMessage(ErrorMessageType.InvalidReturnPosInstance);
            }
        }
    }
}
