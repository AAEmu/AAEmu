using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.World.Interactions;

public class CropHarvest : IWorldInteraction
{
    private static Logger Logger = LogManager.GetCurrentClassLogger();

    public void Execute(BaseUnit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
        uint skillId, uint doodadId, DoodadFuncTemplate objectFunc = null)
    {
        if (target is Doodad doodad)
        {
            if (PublicFarmManager.Instance.InPublicFarm(doodad.Transform.WorldId, doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y))
            {
                if (PublicFarmManager.Instance.IsProtected(doodad))
                {
                    if(caster is Character character)
                    {
                        character.SendErrorMessage(ErrorMessageType.CannotHarvestYet);
                        Logger.Debug($"This should never happen character {character.Name} attempted to bypass harvest protection (clienthacks?)");
                        return;
                    }
                }
            }

            doodad.Use(caster, skillId);
        }
    }
}
