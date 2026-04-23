using System.Numerics;

using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics.Forces;

/// <summary>
/// Whirlpool pull for ships: when a ship has buff 1918, apply a small horizontal drift toward the nearest active whirlpool doodad (3086).
/// Implemented as a PreStep "drift" similar to river flow (position delta), to avoid touching ship controller logic.
/// </summary>
public sealed class WhirlpoolShipPull(World world, Func<WorldInstance> getWorld) : ForceGenerator(world)
{
    private const uint WhirlpoolBuffId = 1918;
    private const uint WhirlpoolDoodadTemplateId = 3086;

    /// <summary>Base pull speed toward center in m/s.</summary>
    public static float PullSpeedMetersPerSec = 3f;

    /// <summary>
    /// Minimum pull multiplier at full throttle.
    /// 1.0 = full pull even at full throttle; 0.0 = no pull at full throttle.
    /// </summary>
    public static float PullAtFullThrottleMulMin = 0.5f;

    /// <summary>Minimum horizontal distance (m) before applying pull (prevents jitter near exact center).</summary>
    public static float MinDistanceMeters = 0.35f;

    public override void PreStep(float timeStep)
    {
        if (timeStep <= 0f)
            return;

        var gameWorld = getWorld();
        if (gameWorld == null)
            return;

        // Snapshot active whirlpools once per step.
        var whirlpools = new List<Vector2>();
        foreach (var d in gameWorld.GetAllDoodads())
        {
            if (d is { IsVisible: true, TemplateId: WhirlpoolDoodadTemplateId })
                whirlpools.Add(new Vector2(d.Transform.World.Position.X, d.Transform.World.Position.Y));
        }

        if (whirlpools.Count == 0)
            return;

        var baseSpeed = PullSpeedMetersPerSec;
        if (baseSpeed <= 0f)
            return;

        var minDistSq = MinDistanceMeters * MinDistanceMeters;

        foreach (var slave in gameWorld.GetAllSlaves())
        {
            if (slave?.RigidBody is not { } body || body.IsStatic || !body.IsActive)
                continue;

            if (!slave.Buffs.CheckBuff(WhirlpoolBuffId))
                continue;

            // Do not pull while grounded/beached.
            var groundedNow = slave.GroundContactLatched || slave.CachedFloorLevel > slave.CachedWaterSurface;
            if (groundedNow)
                continue;

            var shipPos = new Vector2(slave.Transform.World.Position.X, slave.Transform.World.Position.Y);

            // Find nearest whirlpool.
            var bestDistSq = float.MaxValue;
            var bestCenter = default(Vector2);
            foreach (var c in whirlpools)
            {
                var distSq = Vector2.DistanceSquared(shipPos, c);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestCenter = c;
                }
            }

            if (bestDistSq <= minDistSq)
                continue;

            var toCenter = bestCenter - shipPos;
            var dir = Vector2.Normalize(toCenter);

            // Reduce pull when driver applies throttle so ships can still escape.
            var throttleAbs01 = Math.Clamp(MathF.Abs(slave.Throttle) / 127f, 0f, 1f);
            var pullMul = 1f + (PullAtFullThrottleMulMin - 1f) * throttleAbs01;
            var speed = baseSpeed * pullMul;
            if (speed <= 0f)
                continue;

            var dxy = dir * (speed * timeStep);

            // Physics rigid-body horizontal plane is XZ; rigid-body Z maps to world Y.
            body.Position += new JVector(dxy.X, 0f, dxy.Y);
        }
    }
}

