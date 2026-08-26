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

    // Hull damage while pulled by a whirlpool: 1% HP per second.
    private const float HullDamageIntervalSec = 1f;
    private const int HullDamagePercentPerTick = 1;
    private readonly Dictionary<uint, float> _hullDamageAccSecBySlaveObjId = new();

    // Per-tick buffers (physics hot path): reused to avoid GC churn.
    private readonly List<Vector2> _whirlpools = new(16);
    private readonly HashSet<uint> _affectedThisTick = new();
    private readonly List<uint> _keysToRemove = new(16);

    /// <summary>Base pull speed toward center in m/s.</summary>
    public const float PullSpeedMetersPerSec = 3f;

    /// <summary>
    /// Minimum pull multiplier at full throttle.
    /// 1.0 = full pull even at full throttle; 0.0 = no pull at full throttle.
    /// </summary>
    public const float PullAtFullThrottleMulMin = 0.5f;

    /// <summary>Minimum horizontal distance (m) before applying pull (prevents jitter near exact center).</summary>
    public const float MinDistanceMeters = 0.35f;

    public override void PreStep(float timeStep)
    {
        if (timeStep <= 0f)
            return;

        var gameWorld = getWorld();
        if (gameWorld == null)
            return;

        // Avoid scanning all doodads if no ship is currently affected.
        var slaves = gameWorld.GetAllSlaves();
        var anyAffectedShip = false;
        foreach (var s in slaves)
        {
            if (s?.Buffs.CheckBuff(WhirlpoolBuffId) == true)
            {
                anyAffectedShip = true;
                break;
            }
        }
        if (!anyAffectedShip)
            return;

        // Clamp dt to avoid a long hitch applying too many damage loops at once.
        var dt = Math.Clamp(timeStep, 0f, 0.5f);

        // Snapshot active whirlpools once per step.
        _whirlpools.Clear();
        foreach (var d in gameWorld.GetAllDoodads())
        {
            if (d is { IsVisible: true, TemplateId: WhirlpoolDoodadTemplateId })
                _whirlpools.Add(new Vector2(d.Transform.World.Position.X, d.Transform.World.Position.Y));
        }

        if (_whirlpools.Count == 0)
            return;

        var baseSpeed = PullSpeedMetersPerSec;
        if (baseSpeed <= 0f)
            return;

        var minDistSq = MinDistanceMeters * MinDistanceMeters;

        // Track ships that are currently affected this tick, to cleanup stale accumulators.
        _affectedThisTick.Clear();

        foreach (var slave in slaves)
        {
            if (slave?.RigidBody is not { } body || body.MotionType == MotionType.Static || !body.IsActive)
                continue;

            if (!slave.Buffs.CheckBuff(WhirlpoolBuffId))
                continue;

            // Do not pull while grounded/beached.
            var groundedNow = slave.GroundContactLatched || slave.CachedFloorLevel > slave.CachedWaterSurface;
            if (groundedNow)
                continue;

            _affectedThisTick.Add(slave.ObjId);

            var shipPos = new Vector2(slave.Transform.World.Position.X, slave.Transform.World.Position.Y);

            // Find nearest whirlpool.
            var bestDistSq = float.MaxValue;
            var bestCenter = default(Vector2);
            foreach (var c in _whirlpools)
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

            var dxy = dir * (speed * dt);

            // Physics rigid-body horizontal plane is XZ; rigid-body Z maps to world Y.
            body.Position += new JVector(dxy.X, 0f, dxy.Y);

            // Hull damage while affected by the whirlpool pull.
            _hullDamageAccSecBySlaveObjId.TryGetValue(slave.ObjId, out var acc);
            acc += dt;
            while (acc >= HullDamageIntervalSec)
            {
                acc -= HullDamageIntervalSec;
                slave.ApplyShipHullCollisionDamage(slave, HullDamagePercentPerTick);
            }
            _hullDamageAccSecBySlaveObjId[slave.ObjId] = acc;
        }

        if (_hullDamageAccSecBySlaveObjId.Count > 0)
        {
            _keysToRemove.Clear();
            foreach (var key in _hullDamageAccSecBySlaveObjId.Keys)
            {
                if (!_affectedThisTick.Contains(key))
                    _keysToRemove.Add(key);
            }
            foreach (var key in _keysToRemove)
                _hullDamageAccSecBySlaveObjId.Remove(key);
        }
    }
}

