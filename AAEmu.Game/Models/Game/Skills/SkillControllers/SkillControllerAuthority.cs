using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using GameMate = AAEmu.Game.Models.Game.Units.Mate;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

public static class SkillControllerAuthority
{
    public static bool CanControl(Character character, uint objId)
    {
        if (character == null)
            return false;

        // A character always controls itself, and that answer does not need a world to look a unit up in:
        // the self case used to be refused whenever ParentWorld was unset, which is only ever true of a
        // character that is not in a world at all.
        if (objId == character.ObjId)
            return true;

        if (character.ParentWorld == null)
            return false;

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
