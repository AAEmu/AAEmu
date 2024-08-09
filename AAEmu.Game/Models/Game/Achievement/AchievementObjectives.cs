namespace AAEmu.Game.Models.Game.Achievement
{
    public partial class AchievementObjectives
    {
        public uint Id { get; set; }
        public uint AchievementId { get; set; }
        public bool OrUnitReqs { get; set; }
        public uint RecordId { get; set; }

        public virtual Achievements Achievement { get; set; }
    }
}
