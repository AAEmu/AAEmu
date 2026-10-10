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
    /// An NPC may drive itself, unless its position belongs to the zone: under zone authority a mirrored
    /// NPC is moved by the zone's movement stream, and a second, server-driven movement for the same unit
    /// fights that stream — the zone keeps pulling the unit back, the controller never reaches its end
    /// position, and the client sees the unit snap between the two at the controller's tick rate.
    /// A player's own leap or dash is the same movement, and the server has to own the position the skill
    /// ends at — the client predicts it locally and is corrected by the movement packets the controller
    /// sends — so a Character caster is allowed for a unit it controls: itself, its mount or its ship.
    /// Anything else (a controller that would move a stranger) is refused.
    /// </remarks>
    public static bool CanCreateController(BaseUnit caster, BaseUnit owner) =>
        CanCreateController(caster, owner, WorldIntegration.ZoneAuthority);

    /// <summary>
    /// <see cref="CanCreateController(BaseUnit, BaseUnit)"/> with the zone-authority switch passed in.
    /// </summary>
    public static bool CanCreateController(BaseUnit caster, BaseUnit owner, bool zoneAuthority)
    {
        switch (caster)
        {
            case Npc:
                return !ZoneOwnsPosition(caster, zoneAuthority);
            case Character character:
                return SkillControllerAuthority.CanControl(character, owner?.ObjId ?? character.ObjId);
            default:
                return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="unit"/>'s position is moved by the zone rather than by this server: an NPC
    /// mirrored from a zone while the zone is the simulation authority.
    /// </summary>
    public static bool ZoneOwnsPosition(BaseUnit unit, bool zoneAuthority) =>
        zoneAuthority && unit is Npc { IsZoneMirror: true };

    /// <summary>
    /// Whether a controller cast whose controller was not built has to be closed immediately.
    /// </summary>
    /// <remarks>
    /// A leap/dash with a controller carries its whole movement in that controller, and the cast's end is
    /// deferred until the controller is done. When there is no controller to build — no target, or a target
    /// outside the skill's own window — nothing moves the caster and nothing ends the cast: the started cast
    /// stays open and the client keeps the action, so the player cannot move, act, or leave. Closing the cast
    /// in place is what unlocks it.
    /// </remarks>
    public static bool ControllerCastMustCloseNow(bool hasController, bool controllerRealized) =>
        hasController && !controllerRealized;
}
