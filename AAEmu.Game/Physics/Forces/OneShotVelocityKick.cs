using Jitter2;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics.Forces;

/// <summary>Applies a single velocity kick in the next PreStep and self-removes.</summary>
public sealed class OneShotVelocityKick : ForceGenerator
{
    private readonly RigidBody _body;
    private readonly JVector _deltaV;
    private bool _applied;

    public OneShotVelocityKick(World world, RigidBody body, JVector deltaV) : base(world)
    {
        _body = body;
        _deltaV = deltaV;
    }

    public override void PreStep(float timeStep)
    {
        if (_applied)
            return;
        _applied = true;

        if (_body is { MotionType: MotionType.Dynamic })
            _body.Velocity += _deltaV;

        RemoveEffect();
    }
}

