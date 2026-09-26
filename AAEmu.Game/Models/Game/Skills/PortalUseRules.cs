using System.Numerics;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Whether a character may walk into the portal it named. The client sends the use when the character
/// collides with the portal unit, so a portal the character cannot reach has to be refused: the object
/// id is client supplied and resolves any open portal in the world.
/// </summary>
/// <remarks>
/// No shipped content provides a radius to walk a portal from. <c>open_portal_effects.distance</c> is
/// the radius the caster may place the portal inside, not a distance a user may use it from; the
/// portal npc rows carry no interaction range; and six of the eight portal models named by
/// <c>open_portal_effects</c> have no <c>actor_models</c> row, so a contact distance cannot be derived
/// from the model radius either. The guard therefore uses the region neighbourhood that already
/// authorizes the other client named targets — <c>CSGetDoodadManikinSkin</c>,
/// <c>CSDoodadQuestNotiPacket</c> and the static gimmick grasp — rather than a distance that no
/// content or protocol value provides.
/// </remarks>
public static class PortalUseRules
{
    /// <summary>
    /// Whether <paramref name="portalPosition"/> lies in the neighbourhood the character can see the
    /// world from. A character the world has not placed in a region yet is refused rather than
    /// trusted, so a missing region fails towards refusing the use.
    /// </summary>
    /// <remarks>
    /// The neighbourhood is a ground-plane grid, so height does not narrow it, and the cell index is an
    /// integer division that truncates towards zero, which puts the bound roughly one region further
    /// out below the origin than above it. Both properties belong to the shared neighbourhood
    /// calculation rather than to this rule.
    /// </remarks>
    public static bool IsReachableFrom(GameObject character, Vector3 portalPosition) =>
        WorldManager.IsInNeighborhood(character, portalPosition);
}
