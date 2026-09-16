using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

/// <summary>
/// Who may get a skill controller. A controller drives its owner's server-side position and broadcasts the
/// movement for it (<see cref="LinearMoveSkillController"/>), so the unit it moves has to be one the caster
/// is allowed to drive.
/// </summary>
public static class SkillControllerRules
{
    /// <summary>
    /// Whether <paramref name="caster"/> may have a controller created for <paramref name="owner"/>.
    /// </summary>
    /// <remarks>
    /// NPCs have always been allowed: the zone's AI asks for a controller cast and the server owns the NPC's
    /// position outright. A player's own leap or dash is the same movement, and the server has to own the
    /// position the skill ends at — the client predicts it locally and is corrected by the movement packets
    /// the controller sends — so a Character caster is allowed for a unit it controls: itself, its mount or
    /// its ship. Anything else (a controller that would move a stranger) is refused.
    /// </remarks>
    public static bool CanCreateController(BaseUnit caster, BaseUnit owner)
    {
        switch (caster)
        {
            case Npc:
                return true;
            case Character character:
                return SkillControllerAuthority.CanControl(character, owner?.ObjId ?? character.ObjId);
            default:
                return false;
        }
    }
}
