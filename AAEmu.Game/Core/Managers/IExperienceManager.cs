namespace AAEmu.Game.Core.Managers;

public interface IExperienceManager : ILoadable
{
    byte MaxPlayerLevel { get; }
    byte MaxMateLevel { get; }
    void Load(IExperienceLevelTemplateLoader loader, byte playerLevelCap, byte mateLevelCap);
    int GetExpForLevel(byte level, bool mate = false);
    byte GetLevelFromExp(int exp, out int overflow, bool mate = false);
    byte GetLevelFromExp(int exp, byte currentLevel, out int overflow, bool mate = false);
    int GetExpNeededToGivenLevel(int currentExp, byte targetLevel, bool mate = false);
    int GetSkillPointsForLevel(byte level);
}
