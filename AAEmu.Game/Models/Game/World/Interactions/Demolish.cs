using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.World.Interactions;

public class Demolish : ISkillObjectWorldInteraction
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public void Execute(BaseUnit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
        uint skillId, uint doodadId, DoodadFuncTemplate objectFunc = null)
    {
        Execute(caster, casterType, target, targetType, skillId, doodadId, (SkillObject)null);
    }

    public void Execute(BaseUnit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
        uint skillId, uint doodadId, SkillObject skillObject)
    {
        if (target is not House house || caster is not Character character)
            return;

        // Full Kit Demolition asks for the house back as a completed design. This server cannot make
        // one yet, and a plain demolition in its place would cost the player the finished building,
        // so the request is refused and the house is left standing.
        if (PackageRefusal(house, skillObject) is { } refusal)
        {
            Logger.Warn("Demolish: {0} asked for Full Kit Demolition of house {1} ({2} certificates), which is not supported - refused",
                character.Name, house.Id, ((SkillObjectHouseDemolish)skillObject).SealCount);
            character.SendErrorMessage(refusal);
            return;
        }

        HousingManager.Instance.Demolish(character.Connection, house, false, false);
    }

    /// <summary>
    /// The error a Full Kit Demolition request gets, or null for a plain demolition.
    /// </summary>
    internal static ErrorMessageType? PackageRefusal(House house, SkillObject skillObject)
    {
        if (skillObject is not SkillObjectHouseDemolish { Package: true })
            return null;

        return house.CurrentStep != -1
            ? ErrorMessageType.HousePackageDemolishFailedUnderConstruction
            : ErrorMessageType.InternalError;
    }
}
