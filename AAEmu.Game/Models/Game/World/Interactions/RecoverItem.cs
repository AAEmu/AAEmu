using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class RecoverItem : IWorldInteraction
    {
        public void Execute(Unit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
            uint skillId, uint doodadId, DoodadFuncTemplate objectFunc)
        {
            if ((target is Doodad doodad) && doodad.AllowRemoval())
            {
                doodad.Use(caster, skillId, 0, "DoodadFuncRecoverItem");
            }
            else
            {
                caster.SendErrorMessage(ErrorMessageType.FailedToUseItem);
                caster.InterruptSkills();
            }
        }
    }
}
