using System.Numerics;

using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Where an <c>OpenPortalEffect</c> may place its portal. The client sends the position it wants the
/// portal at, and the effect's <c>distance</c> column is the radius the owner has to stay inside.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: all 11 rows of <c>open_portal_effects</c> use distance 3.0.
/// </remarks>
public static class OpenPortalRules
{
    /// <summary>
    /// Whether the portal position the client asked for is within <paramref name="distance"/> of its
    /// owner. The check is a radius on the ground plane — the same <see cref="MathUtil.CalculateDistance(Vector3, Vector3, bool)"/>
    /// measure the rest of the skill code uses.
    /// </summary>
    public static bool IsWithinOpenDistance(Vector3 portalPosition, Vector3 ownerPosition, float distance) =>
        MathUtil.CalculateDistance(portalPosition, ownerPosition) <= distance;
}
