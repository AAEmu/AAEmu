using System;

using NLog;

namespace AAEmu.Game.Models.Game.AI.v2.Framework;

/// <summary>
/// Represents an AI state. Called as such because of naming in the game's files.
/// </summary>
public abstract class Behavior
{
    protected static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    protected DateTime _delayEnd;
    protected float _nextTimeToDelay;
    protected float _maxWeaponRange;

    public NpcAi Ai { get; set; }
    public abstract void Enter();
    public abstract void Tick(TimeSpan delta);
    public abstract void Exit();

    public Behavior AddTransition(TransitionEvent on, BehaviorKind kind)
    {
        return AddTransition(new Transition(on, kind));
    }

    public Behavior AddTransition(Transition transition)
    {
        return Ai.AddTransition(this, transition);
    }

    public float CheckSightRangeScale(float value)
    {
        var sightRangeScale = value * Ai.Owner.Template.SightRangeScale;
        if (sightRangeScale < value)
        {
            sightRangeScale = value;
        }

        return sightRangeScale;
    }
}
