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
    /// Holds an altitude instead of resting on terrain: <c>movement_id</c> 2 (<c>shark</c>, the models
    /// that move in 3D), and <c>fly_mode</c> for the further models that fly with <c>movement_id</c> 0 -
    /// kestrels, watchers, wraiths, wisps and ghost ships.
    /// </summary>
    public static bool HoldsAltitude(ActorModel model) =>
        model is { MovementId: 2 } or { FlyMode: true };

    /// <summary>
    /// <c>actor_models.underwater_creature</c> - sharks, jellyfish, kraken, seafolk. Nothing else marks
    /// them: of the 41 swimmer models that are not already off-ground, 27 carry <c>movement_id</c> 0,
    /// 13 carry 1 and one carries 3, so the movement type alone reads them as ground walkers.
    /// </summary>
    public static bool SwimsUnderwater(ActorModel model) =>
        model is { UnderwaterCreature: true };

    /// <summary>
    /// True for a model the dedicate has to simulate off the ground. A swimmer counts: snapping it to
    /// the terrain puts a shark on the sea floor, and without the flying state the zone walks it there
    /// instead of swimming it. <c>movement_id</c> 3 (<c>stick_to_ground</c>) deliberately does not count
    /// on its own - a model that sticks to the ground belongs on it.
    /// </summary>
    public static bool SimulatesOffGround(ActorModel model) =>
        HoldsAltitude(model) || SwimsUnderwater(model);

    /// <summary>
    /// The speed a unit walks at. A prefab - a siege place, a portal, a chest, a wall - is a static
    /// prop with no actor model of its own, so it has no speed at all: the fallback for a model that is
    /// simply missing must not turn into "it can walk".
    /// </summary>
    public static float MoveSpeedFor(bool isPrefabModel, float actorMoveSpeed) =>
        isPrefabModel ? 0f : actorMoveSpeed;

    /// <summary>
    /// The two flags a spawned unit carries, from one model. They are not the same question and must not
    /// be answered with the same call: <c>CanFly</c> is what the stance picks the flight pose from, so a
    /// swimmer given <c>CanFly</c> flies instead of swimming, while the off-ground treatment it does
    /// need is the union (<see cref="SimulatesOffGround"/>).
    /// </summary>
    public static (bool CanFly, bool IsSwimmer) SpawnFlags(ActorModel model) =>
        (HoldsAltitude(model), SwimsUnderwater(model));
}
