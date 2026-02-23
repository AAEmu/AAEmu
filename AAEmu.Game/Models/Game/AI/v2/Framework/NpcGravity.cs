using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.SkillControllers;

namespace AAEmu.Game.Models.Game.AI.v2.Framework;

/// <summary>
/// Instant ground-snap system for NPCs. Applied each AI tick to keep
/// non-flying NPCs on the navmesh/terrain surface. No gravity simulation —
/// the height chain (NavMesh terrain → GeoData → HeightMap) is authoritative.
/// </summary>
public static class NpcGravity
{
    /// <summary>
    /// Minimum height difference before snapping (prevents micro-jitter).
    /// </summary>
    private const float SnapThreshold = 0.1f;

    /// <summary>
    /// Minimum Z change to be considered a "large" snap for oscillation detection.
    /// Changes smaller than this are always allowed (normal terrain following).
    /// </summary>
    private const float OscillationThreshold = 1.0f;

    /// <summary>
    /// Snaps an NPC to the navmesh/terrain surface if its Z differs.
    /// Should be called once per AI tick.
    /// </summary>
    /// <returns>True if the NPC position was adjusted</returns>
    public static bool ApplyGravity(Npc npc, TimeSpan delta)
    {
        if (npc == null || npc.CanFly || npc.IsDead)
            return false;

        // Skip if NPC is currently executing a skill that controls movement
        if ((npc.ActiveSkillController?.State ?? SkillController.SCState.Ended) == SkillController.SCState.Running)
            return false;

        var currentPos = npc.Transform.World.Position;
        var zoneId = npc.Transform.ZoneId;

        // Get ground height at current XY position.
        // Chain: BuildingFloors → NavMesh (terrain only) → GeoData → HeightMap
        var groundZ = WorldManager.Instance.GetHeight(zoneId, currentPos.X, currentPos.Y, currentPos.Z);

        // If no ground data available, skip to avoid pushing NPC underground
        if (groundZ <= 0f)
            return false;

        var diff = currentPos.Z - groundZ;
        if (MathF.Abs(diff) <= SnapThreshold)
            return false;

        // Anti-oscillation: detect and prevent Z flickering between two heights.
        // If the last gravity snap moved the NPC in one direction, and this snap
        // would reverse it by more than OscillationThreshold, it's likely caused
        // by competing height sources. In that case, keep the current Z.
        if (npc.LastGravityZ != 0f && MathF.Abs(diff) > OscillationThreshold)
        {
            var lastDirection = currentPos.Z - npc.LastGravityZ;
            var newDirection = groundZ - currentPos.Z;

            if (lastDirection * newDirection < 0f &&
                MathF.Abs(lastDirection) > OscillationThreshold)
            {
                return false;
            }
        }

        // Record current Z before snapping (for oscillation detection next tick)
        npc.LastGravityZ = currentPos.Z;

        // Instant snap to ground surface
        npc.Transform.Local.SetHeight(groundZ);
        return true;
    }
}
