using System.Numerics;

namespace AAEmu.Game.Models.Game.Gimmicks;

#pragma warning disable CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor.
public class GimmickMovementProjectile(Gimmick owner) : GimmickMovementHandler(owner)
#pragma warning restore CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor.
{
    public override void Tick(TimeSpan delta)
    {
        base.Tick(delta);

        if (owner.Template == null)
            return;

        var dt = (float)delta.TotalSeconds;
        if (dt <= 0f)
            return;

        var vel = owner.Vel;
        var pos = owner.Transform.World.Position;

        vel.Z -= owner.Template.Gravity * dt;

        var air = owner.Template.AirResistance;
        if (air > 0f)
        {
            var k = MathF.Max(0f, 1f - air * dt);
            vel *= k;
        }

        pos += vel * dt;

        owner.Vel = vel;
        owner.Transform.World.Position = pos;
    }
}

