using System.Numerics;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics;

namespace AAEmu.Game.Models.Game.Gimmicks;

#pragma warning disable CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor.
public class GimmickMovementProjectile(Gimmick owner) : GimmickMovementHandler(owner)
#pragma warning restore CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor.
{
    public override void Tick(TimeSpan delta)
    {
        base.Tick(delta);

        var template = owner.Template;
        if (template == null)
            return;

        // Movement must never crash the global tick loop
        var worldTf = owner.Transform?.World;
        if (worldTf == null)
            return;

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
        {
            const float projectileRadius = 0.25f;
            const float shipQueryRadius = 120f;
            var nearbyShips = WorldManager.GetAround<Slave>(owner, shipQueryRadius, false);
            foreach (var ship in nearbyShips)
            {
                if (!ship.Template.IsABoat())
                    continue;
                if (ship.RigidBody is null || ship.ShipController?.ShipModel is null)
                    continue;
                if (!ShipSiegeAoEHit.TrySiegePointHitsShipMassBoxXz(pos.X, pos.Y, projectileRadius, ship))
                    continue;

                worldTf.Position = pos;
                owner.Vel = Vector3.Zero;

                var impactSpeed = vel.Length();
                if (impactSpeed >= template.CollisionMinSpeed)
                {
                    var skillId = template.CollisionSkillId != 0 ? template.CollisionSkillId : template.SkillId;
                    owner.TriggerSkill(skillId);
                }

                if (template.DisappearByCollision)
                    owner.Spawner?.Despawn(owner);
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
                worldTf.Position = pos;
                owner.Vel = Vector3.Zero;

                var impactSpeed = vel.Length();
                if (impactSpeed >= template.CollisionMinSpeed)
                {
                    var skillId = template.CollisionSkillId != 0 ? template.CollisionSkillId : template.SkillId;
                    owner.TriggerSkill(skillId);
                }

                if (template.DisappearByCollision)
                    owner.Spawner?.Despawn(owner);
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
                    worldTf.Position = pos;
                    owner.Vel = Vector3.Zero;

                    var impactSpeed = vel.Length();
                    if (impactSpeed >= template.CollisionMinSpeed)
                    {
                        var skillId = template.CollisionSkillId != 0 ? template.CollisionSkillId : template.SkillId;
                        owner.TriggerSkill(skillId);
                    }

                    if (template.DisappearByCollision)
                        owner.Spawner?.Despawn(owner);
                    return;
                }
            }
        }

        owner.Vel = vel;
        worldTf.Position = pos;
    }
}

