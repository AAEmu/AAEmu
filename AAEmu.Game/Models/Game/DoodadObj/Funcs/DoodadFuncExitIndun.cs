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
                if (ReturnPointId == 0 && character.MainWorldPosition != null)
                {
                    IndunManager.Instance.RequestLeave(character);
                }
                else
                {
                    // TODO in db not have a entries, but we can change this xD
                    Logger.Info("DoodadFuncExitIndun, Not have return point!");
                    character.SendErrorMessage(ErrorMessageType.InvalidReturnPosInstance); // ошибка, не можете выйти сейчас из данжона
                    //character.SendErrorMessage(ErrorMessageType.TryLaterInstance); // ошибка данжона, пробуй еще раз
                    //character.SendErrorMessage(ErrorMessageType.InvalidStateInstance); // данжон уже загружен
                    //character.SendErrorMessage(ErrorMessageType.ProhibitedInInstance); // нельзя это сделать внутри данжона
                    //character.SendErrorMessage(ErrorMessageType.InstanceVisitLimit); // Ты израсходовал лимит на вход в данжон. Пробуй позже.
                }
            }
        }
}
