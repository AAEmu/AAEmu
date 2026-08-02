using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using GameMate = AAEmu.Game.Models.Game.Units.Mate;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

public static class SkillControllerAuthority
{
    public static bool CanControl(Character character, uint objId)
    {
        if (character?.ParentWorld == null)
            return false;

        if (objId == character.ObjId)
            return true;

        return character.ParentWorld.GetBaseUnit(objId) switch
        {
            GameMate mate => mate.OwnerObjId == character.ObjId
                             || mate.Passengers.Values.Any(passenger => passenger?._objId == character.ObjId),
            Slave slave => slave.Summoner?.ObjId == character.ObjId
                           || slave.OwnerObjId == character.ObjId
                           || slave.AttachedCharacters.Values.Any(passenger => passenger?.ObjId == character.ObjId),
            _ => false
        };
    }
}
