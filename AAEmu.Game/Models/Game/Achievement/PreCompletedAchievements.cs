namespace AAEmu.Game.Models.Game.Achievement
{
    public partial class PreCompletedAchievements
    {
        public uint Id { get; set; }
        public uint CompletedAchievementId { get; set; }
        public uint MyAchievementId { get; set; }
    }
}
