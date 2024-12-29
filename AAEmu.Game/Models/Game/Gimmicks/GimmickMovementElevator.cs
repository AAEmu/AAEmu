using System;
using System.Numerics;

namespace AAEmu.Game.Models.Game.Gimmicks;

#pragma warning disable CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor. The value might be captured by the base class as well.
public class GimmickMovementElevator(Gimmick owner) : GimmickMovementHandler(owner)
#pragma warning restore CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor. The value might be captured by the base class as well.
{
    public override void Tick(TimeSpan delta)
    {
        base.Tick(delta);

        var deltaTime = (float)delta.TotalSeconds;
        const float maxVelocity = 4.5f;
        var movingDistance = deltaTime * 0.5f;

        var position = owner.Transform.World.Position;
        var velocityZ = owner.Vel.Z;

        var middleTarget = position with { Z = owner.Spawner.MiddleZ };
        var topTarget = position with { Z = owner.Spawner.TopZ };
        var bottomTarget = position with { Z = owner.Spawner.BottomZ };

        var isMovingDown = owner.moveDown;
        var isInMiddleZ = owner.Spawner.MiddleZ > 0;

        if (isInMiddleZ)
        {
            if (position.Z < owner.Spawner.MiddleZ && owner.Vel.Z >= 0 && !isMovingDown)
                owner.MoveAlongZAxis(owner, ref position, middleTarget, maxVelocity, deltaTime, movingDistance, ref velocityZ, ref isMovingDown);
            else if (position.Z < owner.Spawner.TopZ && owner.Vel.Z >= 0 && !isMovingDown)
                owner.MoveAlongZAxis(owner, ref position, topTarget, maxVelocity, deltaTime, movingDistance, ref velocityZ,
                    ref isMovingDown);
            else if (position.Z > owner.Spawner.MiddleZ && owner.Vel.Z <= 0 && isMovingDown)
                owner.MoveAlongZAxis(owner, ref position, middleTarget, maxVelocity, deltaTime, movingDistance,
                    ref velocityZ, ref isMovingDown);
            else
                owner.MoveAlongZAxis(owner, ref position, bottomTarget, maxVelocity, deltaTime, movingDistance,
                    ref velocityZ, ref isMovingDown);
        }
        else
        {
            if (position.Z < owner.Spawner.TopZ && owner.Vel.Z >= 0)
                owner.MoveAlongZAxis(owner, ref position, topTarget, maxVelocity, deltaTime, movingDistance, ref velocityZ,
                    ref isMovingDown);
            else
                owner.MoveAlongZAxis(owner, ref position, bottomTarget, maxVelocity, deltaTime, movingDistance,
                    ref velocityZ, ref isMovingDown);
        }

        owner.Transform.Local.SetHeight(position.Z);
    }

    public override void AfterMove(TimeSpan delta, Vector3 deltaPosition)
    {
        base.AfterMove(delta, deltaPosition);
        if (deltaPosition.Length() > 0.01f)
        {
            return;
        }

        owner.WaitTime = DateTime.UtcNow.AddSeconds(owner.Spawner.WaitTime);
        owner.moveDown = !owner.moveDown;
    }
}
