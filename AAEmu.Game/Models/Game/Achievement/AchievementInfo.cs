using System;

namespace AAEmu.Game.Models.Game.Achievement
{
    public partial class AchievementInfo
    {
        public uint Id { get; set; }
        public uint Amount { get; set; }
        public DateTime Complete { get; set; }
    }
}
