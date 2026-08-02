namespace AAEmu.Game.Models.Game.Models;

public class ActorModel : Model
{
    public float Radius { get; set; }
    public float Height { get; set; }
    public int MovementId { get; set; } // 0 = normal, 1 = mounts, 2 = bird/fish (ignores gravity), 3 = sunk in the ground / hidden underground

    /// <summary>
    /// actor_models.fly_mode — the model holds an altitude instead of resting on terrain. Independent
    /// of MovementId: 8 models (kestrels, watchers, wraiths, wisps, ghost ships) fly with MovementId 0.
    /// </summary>
    public bool FlyMode { get; set; }

    /// <summary>actor_models.underwater_creature — sharks, jellyfish, kraken, seafolk.</summary>
    public bool UnderwaterCreature { get; set; }

    public Dictionary<GameStanceType, GameStance> Stances { get; set; } = [];
}
