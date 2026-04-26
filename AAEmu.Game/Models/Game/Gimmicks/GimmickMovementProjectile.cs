using System.Numerics;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics;

namespace AAEmu.Game.Models.Game.Gimmicks;

#pragma warning disable CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor.
public class GimmickMovementProjectile(Gimmick owner) : GimmickMovementHandler(owner)
#pragma warning restore CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor.
{
    private bool _stuckAfterImpact;
    private Vector3 _impactPos;

    public override void Tick(TimeSpan delta)
    {
        base.Tick(delta);

        var template = owner.Template;
        if (template == null)
            return;

        // When CollisionUnitOnly is set, we still stop on world barriers (water/ground),
        // but we only treat collisions with Units as a "detonation" (collision-skill / disappear-by-collision).
        var detonateOnlyOnUnits = template.CollisionUnitOnly;

        // Movement must never crash the global tick loop
        var worldTf = owner.Transform?.World;
        if (worldTf == null)
            return;

        void StickAtImpact(Vector3 impactPos)
        {
            _stuckAfterImpact = true;
            _impactPos = impactPos;
            worldTf.Position = impactPos;
            owner.Vel = Vector3.Zero;
        }

        // After impact/detonation we keep the gimmick in-place (fade-out / lifetime),
        // but we must not keep integrating gravity, otherwise it "slides" down surfaces.
        if (_stuckAfterImpact)
        {
            worldTf.Position = _impactPos;
            owner.Vel = Vector3.Zero;
            return;
        }

        var dt = (float)delta.TotalSeconds;
        if (dt <= 0f)
            return;

        var vel = owner.Vel;
        var pos = worldTf.Position;
        var oldPos = pos;

        vel.Z -= template.Gravity * dt;

        var air = template.AirResistance;
        if (air > 0f)
        {
            var k = MathF.Max(0f, 1f - air * dt);
            vel *= k;
        }

        pos += vel * dt;

        // Detonate on ship hull collision (mass-box OBB in XY).
        // Prevent immediate self-hit when firing from a ship deck (projectile starts inside the hull OBB).
        if (owner.TotalLifeTime.TotalMilliseconds >= 200)
        {
            const float projectileRadius = 0.5f;
            const float shipQueryRadius = 120f;
            var nearbyShips = WorldManager.GetAround<Slave>(owner, shipQueryRadius, false);
            foreach (var ship in nearbyShips)
            {
                if (!ship.Template.IsABoat())
                    continue;
                if (ship.RigidBody is null || ship.ShipController?.ShipModel is null)
                    continue;

                // Cheap early-out: if we're far from the ship mass-box center, skip the expensive OBB distance test.
                // World horizontal plane is X/Y; ship mass-box math uses X/Z which maps to world X/Y.
                ShipShipInteraction.GetMassBoxCenterXz(ship.RigidBody, ship.ShipController.ShipModel, ship.Scale, out var cx, out var cz);
                var halfLen = ship.ShipController.ShipModel.MassBoxSizeY * ship.Scale * 0.5f;
                var halfBeam = ship.ShipController.ShipModel.MassBoxSizeX * ship.Scale * 0.5f;
                var maxR = MathF.Sqrt(halfLen * halfLen + halfBeam * halfBeam) + projectileRadius;
                var dx = pos.X - cx;
                var dz = pos.Y - cz;
                if (dx * dx + dz * dz > maxR * maxR)
                    continue;

                if (!ShipSiegeAoEHit.TrySiegePointHitsShipMassBoxXz(pos.X, pos.Y, projectileRadius, ship))
                    continue;

                var impactSpeed = vel.Length();
                StickAtImpact(pos);
                if (impactSpeed >= template.CollisionMinSpeed)
                {
                    var skillId = template.CollisionSkillId != 0 ? template.CollisionSkillId : template.SkillId;
                    owner.TriggerSkill(skillId);
                }

                if (template.DisappearByCollision && impactSpeed >= template.CollisionMinSpeed)
                    owner.Spawner?.Despawn(owner);
                else
                {
                    // Keep the projectile stuck at the impact point until it fades/despawns by lifetime.
                }
                return;
            }
        }

        // Detonate on water surface impact (ocean + river/lake surfaces).
        // We intentionally check this before ground so projectiles explode on splash even if terrain is below.
        var world = owner.ParentWorld;
        if (world != null)
        {
            var probeZ = MathF.Max(oldPos.Z, pos.Z);
            var surfaceZ = world.Water.GetWaterSurface(new Vector3(pos.X, pos.Y, probeZ), out _);
            var crossesSurface = oldPos.Z > surfaceZ && pos.Z <= surfaceZ;
            if (crossesSurface && world.Water.IsWater(new Vector3(pos.X, pos.Y, surfaceZ - 0.01f), out _))
            {
                pos.Z = surfaceZ;
                // On water contact we do not "stick" to the surface; we either detonate or continue sinking.
                worldTf.Position = pos;
                owner.Vel = Vector3.Zero;

                if (!detonateOnlyOnUnits)
                {
                    var impactSpeed = vel.Length();
                    if (impactSpeed >= template.CollisionMinSpeed)
                    {
                        var skillId = template.CollisionSkillId != 0 ? template.CollisionSkillId : template.SkillId;
                        owner.TriggerSkill(skillId);
                    }

                    if (template.DisappearByCollision && impactSpeed >= template.CollisionMinSpeed)
                        owner.Spawner?.Despawn(owner);
                }

                return;
            }
        }

        if (!template.NoGroundCollider)
        {
            if (world != null)
            {
                var floorZ = world.GetHeight(pos.X, pos.Y);
                if (oldPos.Z > floorZ && pos.Z <= floorZ)
                {
                    pos.Z = floorZ;
                    StickAtImpact(pos);

                    if (!detonateOnlyOnUnits)
                    {
                        var impactSpeed = vel.Length();
                        if (impactSpeed >= template.CollisionMinSpeed)
                        {
                            var skillId = template.CollisionSkillId != 0 ? template.CollisionSkillId : template.SkillId;
                            owner.TriggerSkill(skillId);
                        }

                        if (template.DisappearByCollision && impactSpeed >= template.CollisionMinSpeed)
                            owner.Spawner?.Despawn(owner);
                    }

                    return;
                }
            }
        }

        owner.Vel = vel;
        worldTf.Position = pos;
    }
}

