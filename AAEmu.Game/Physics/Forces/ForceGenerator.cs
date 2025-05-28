using Jitter2;

namespace AAEmu.Game.Physics.Forces;

/// <summary>
/// Base class for physic effect.
/// </summary>
public class ForceGenerator
{
    protected World _world;

    private readonly World.WorldStep _preStep;
    private readonly World.WorldStep _postStep;

    public ForceGenerator(World world)
    {
        this._world = world;


        // ReSharper disable RedundantDelegateCreation
        _preStep = new World.WorldStep(PreStep);
        _postStep = new World.WorldStep(PostStep);
        // ReSharper enable RedundantDelegateCreation

        world.PostStep += _postStep;
        world.PreStep += _preStep;
    }

    public virtual void PreStep(float timeStep)
    {
    }

    public virtual void PostStep(float timeStep)
    {
    }

    public void RemoveEffect()
    {
        _world.PostStep -= _postStep;
        _world.PreStep -= _preStep;
    }
}
