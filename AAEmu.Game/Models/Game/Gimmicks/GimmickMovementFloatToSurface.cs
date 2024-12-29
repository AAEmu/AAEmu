using System;
using System.Numerics;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Game.Gimmicks;

#pragma warning disable CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor. The value might be captured by the base class as well.
public class GimmickMovementFloatToSurface(Gimmick owner) : GimmickMovementHandler(owner)
#pragma warning restore CS9107 // Parameter is captured into the state of the enclosing type and its value is also passed to the base constructor. The value might be captured by the base class as well.
{
    public override void Tick(TimeSpan delta)
    {
        base.Tick(delta);

        // Expected Upwards distance
        var movement = 2.5f * (float)delta.TotalSeconds; // maximum movement needed to do the 100m in 40 seconds
        var checkPos = owner.Transform.World.Position + new Vector3(0f, 0f, movement + 1f);
        // Check if the new location is still inside water and apply if it is
        if (WorldManager.Instance.GetWorld(owner.Transform.WorldId)?.Water?.IsWater(checkPos) ?? false)
            owner.Transform.Local.Translate(0f, 0f, movement);
    }
}
