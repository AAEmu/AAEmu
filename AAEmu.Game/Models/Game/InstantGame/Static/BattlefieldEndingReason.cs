namespace AAEmu.Game.Models.Game.InstantGame.Static
{
    public enum BattlefieldEndingReason
    {
        Disable = 0x0,
        TimeoverCompareKillCount = 0x1,
        TimeoverCompareRoundWinCount = 0x2,
        TimeoverCompareAlive = 0x3,
        TimeoverDraw = 0x4,
        AchievementKillCount = 0x5,
        AchievementRoundWinCount = 0x6,
        AchievementKillCorpsHead = 0x7,
        AchievementScore = 0x8,
        AchievementAllKillCorps = 0x9,
        UnearnedWin = 0xA,
        TimeoverCompareScore = 0xB,
    }
}
