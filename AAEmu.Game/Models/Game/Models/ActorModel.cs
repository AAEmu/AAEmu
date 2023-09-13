namespace AAEmu.Game.Models.Game.Models
{
    public class ActorModel : Model
    {
        public float Radius { get; set; }
        public float Height { get; set; }
        public int MovementId { get; set; } // 0 = normal, 1 = mounts, 2 = bird/fish (ignores gravity), 3 = sunk in the ground / hidden underground
    }
}