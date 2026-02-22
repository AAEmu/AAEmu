using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.AI.v2.Framework;

/// <summary>
/// Lightweight gravity system for NPCs. Applied each AI tick to pull
/// non-flying NPCs down to terrain when they are above it.
/// </summary>
public static class NpcGravity
{
    /// <summary>
    /// Gravitational acceleration in units/second².
    /// </summary>
    private const float GravityAcceleration = 9.81f;

    /// <summary>
    /// Maximum fall velocity in units/second to prevent excessive speeds.
    /// </summary>
    private const float MaxFallSpeed = 50f;

    /// <summary>
    /// Minimum height above terrain (in units) before gravity is applied.
    /// Prevents micro-corrections on flat terrain.
    /// </summary>
    private const float GroundSnapThreshold = 0.5f;

    /// <summary>
    /// Applies gravity to an NPC if it is above the terrain.
    /// Should be called once per AI tick.
    /// </summary>
    /// <returns>True if the NPC was falling and gravity was applied</returns>
    public static bool ApplyGravity(Npc npc, TimeSpan delta)
    {
        if (npc == null || npc.CanFly || npc.IsDead)
            return false;

        // Skip if NPC is currently executing a skill that controls movement
        if ((npc.ActiveSkillController?.State ?? SkillController.SCState.Ended) == SkillController.SCState.Running)
            return false;

        var currentPos = npc.Transform.World.Position;
        var zoneId = npc.Transform.ZoneId;

        // Get terrain height at current XY position
        var terrainZ = WorldManager.Instance.GetHeight(zoneId, currentPos.X, currentPos.Y, currentPos.Z);

        // If no terrain data available, skip gravity to avoid pushing NPC underground
        if (terrainZ <= 0f)
            return false;

        var heightAboveGround = currentPos.Z - terrainZ;

        // If NPC is below terrain, push up immediately
        if (heightAboveGround < -0.1f)
        {
            npc.Transform.Local.SetHeight(terrainZ);
            npc.FallVelocity = 0f;
            return true;
        }

        // If on or near ground, handle landing
        if (heightAboveGround <= GroundSnapThreshold)
        {
            if (npc.FallVelocity > 0f)
            {
                // Was falling, now landing — snap to ground
                npc.Transform.Local.SetHeight(terrainZ);
                npc.FallVelocity = 0f;
                npc.StopMovement();
                return true;
            }

            // Small correction for slight floating (no broadcast needed)
            if (heightAboveGround > 0.1f)
            {
                npc.Transform.Local.SetHeight(terrainZ);
                return true;
            }

            return false;
        }

        // NPC is above ground — apply gravity
        var deltaSeconds = (float)delta.TotalSeconds;
        if (deltaSeconds <= 0f)
            return false;

        // Accelerate downward
        npc.FallVelocity += GravityAcceleration * deltaSeconds;
        if (npc.FallVelocity > MaxFallSpeed)
            npc.FallVelocity = MaxFallSpeed;

        // Calculate new Z
        var fallDistance = npc.FallVelocity * deltaSeconds;
        var newZ = currentPos.Z - fallDistance;

        // Don't fall below terrain
        if (newZ < terrainZ)
            newZ = terrainZ;

        // Apply new position
        npc.Transform.Local.SetHeight(newZ);

        // Broadcast falling movement to clients
        BroadcastFallingMovement(npc, newZ);

        return true;
    }

    private static void BroadcastFallingMovement(Npc npc, float newZ)
    {
        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        moveType.X = npc.Transform.Local.Position.X;
        moveType.Y = npc.Transform.Local.Position.Y;
        moveType.Z = newZ;
        moveType.VelX = 0;
        moveType.VelY = 0;
        moveType.VelZ = (short)(-npc.FallVelocity * 100);
        moveType.RotationX = 0;
        moveType.RotationY = 0;
        moveType.RotationZ = npc.Transform.Local.ToRollPitchYawSBytesMovement().Item3;
        moveType.ActorFlags = 4;
        moveType.FallVel = (ushort)Math.Min(npc.FallVelocity * 100, ushort.MaxValue);
        moveType.Flags = MoveTypeFlags.Moving;
        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[0] = 0;
        moveType.DeltaMovement[1] = 0;
        moveType.DeltaMovement[2] = -127;
        moveType.Stance = npc.CurrentGameStance;
        moveType.Alertness = npc.CurrentAlertness;
        moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

        npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), false);
    }
}
