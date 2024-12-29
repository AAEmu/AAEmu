using System;
using System.Numerics;

namespace AAEmu.Game.Models.Game.Gimmicks;

public abstract class GimmickMovementHandler(Gimmick owner)
{
    private Gimmick Owner { get; } = owner;

    /// <summary>
    /// Called every tick
    /// </summary>
    /// <param name="delta"></param>
    public virtual void Tick(TimeSpan delta)
    {
        // 
    }

    /// <summary>
    /// Called if the result of a tick did not move since last tick
    /// </summary>
    /// <param name="delta"></param>
    /// <param name="deltaPosition"></param>
    public virtual void AfterMove(TimeSpan delta, Vector3 deltaPosition)
    {
        //
    }
}
