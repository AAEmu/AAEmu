namespace AAEmu.Game.Models.Game.Models;

/// <summary>
/// How an actor model is simulated: whether it holds a position of its own in the air or the water, or
/// rests on the terrain under it. Spawn placement (terrain snapping) and the flying state the World
/// pushes to the dedicate are both keyed on this; the stance a unit takes is not, because a swimmer
/// shows a swim animation where a flier shows the flight one.
/// </summary>
public static class ActorModelRules
{
    /// <summary>
    /// Holds an altitude instead of resting on terrain: <c>movement_id</c> 2 is the birds and fish that
    /// move in 3D, and <c>fly_mode</c> covers the further models that fly with <c>movement_id</c> 0 -
    /// kestrels, watchers, wraiths, wisps and ghost ships.
    /// </summary>
    public static bool HoldsAltitude(ActorModel model) =>
        model is { MovementId: 2 } or { FlyMode: true };

    /// <summary>
    /// <c>actor_models.underwater_creature</c> - sharks, jellyfish, kraken, seafolk. Their rows leave
    /// <c>movement_id</c> at 1 or 3, so nothing else marks them as anything but ground walkers.
    /// </summary>
    public static bool SwimsUnderwater(ActorModel model) =>
        model is { UnderwaterCreature: true };

    /// <summary>
    /// True for a model the dedicate has to simulate off the ground. A swimmer counts: snapping it to
    /// the terrain puts a shark on the sea floor, and without the flying state the zone walks it there
    /// instead of swimming it. <c>movement_id</c> 3 ("sunk in the ground") deliberately does not count -
    /// those models belong on the floor they are sunk into.
    /// </summary>
    public static bool SimulatesOffGround(ActorModel model) =>
        HoldsAltitude(model) || SwimsUnderwater(model);
}
