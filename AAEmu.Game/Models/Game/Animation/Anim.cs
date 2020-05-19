namespace AAEmu.Game.Models.Game.Animation
{
    public class Anim
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public bool Loop { get; set; }
        public AnimCategory Category { get; set; }

        /* Overrides for different situations */
        public string RideUB { get; set; }
        public string HangUB { get; set; }
        public string SwimUB { get; set; }
        public string MoveUB { get; set; }
        public string RelaxedUB { get; set; }
        public string SwimMoveUB { get; set; }

        /* Data from client */
        public int Duration { get; set; }
        public int CombatSyncTime { get; set; }
    }
}
