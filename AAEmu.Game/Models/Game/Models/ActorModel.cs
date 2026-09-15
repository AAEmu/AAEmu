namespace AAEmu.Game.Models.Game.Models;

public class ActorModel : Model
{
    public float Radius { get; set; }
    public float Height { get; set; }
    public int MovementId { get; set; } // enum_movement_type: 0 default, 1 quadruped, 2 shark (moves in 3D), 3 stick_to_ground

    /// <summary>
    /// actor_models.fly_mode — the model holds an altitude instead of resting on terrain. Independent
    /// of MovementId: 8 models (kestrels, watchers, wraiths, wisps, ghost ships) fly with MovementId 0.
    /// </summary>
    public bool FlyMode { get; set; }

    /// <summary>actor_models.underwater_creature — sharks, jellyfish, kraken, seafolk.</summary>
    public bool UnderwaterCreature { get; set; }

    public Dictionary<GameStanceType, GameStance> Stances { get; set; } = [];
}
