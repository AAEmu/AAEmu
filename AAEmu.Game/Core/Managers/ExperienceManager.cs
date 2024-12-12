using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class ExperienceManager : Singleton<ExperienceManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<byte, ExperienceLevelTemplate> _levels;

    // TODO: Put this in the configuration files
    public static byte MaxPlayerLevel => 55;
    public static byte MaxMateLevel => 50;

    public int GetExpForLevel(byte level, bool mate = false)
    {
        var levelTemplate = _levels.GetValueOrDefault(level);
        return mate ? levelTemplate?.TotalMateExp ?? 0 : levelTemplate?.TotalExp ?? 0;
    }

    public byte GetLevelFromExp(int exp, bool mate = false)
    {
        // Loop the levels to find the level for a given exp value
        foreach (var (level, levelTemplate) in _levels)
        {
            if (exp < (mate ? levelTemplate.TotalMateExp : levelTemplate.TotalExp))
                return (byte)(level - 1);
        }
        return 0;
    }

    public int GetExpNeededToGivenLevel(int currentExp, byte targetLevel, bool mate = false)
    {
        var targetExp = GetExpForLevel(targetLevel, mate);
        var diff = targetExp - currentExp;
        return (diff <= 0) ? 0 : diff;
    }

    public int GetSkillPointsForLevel(byte level)
    {
        return _levels.GetValueOrDefault(level)?.SkillPoints ?? 0;
    }

    public void Load()
    {
        _levels = [];
        using (var connection = SQLite.CreateConnection())
        {
            Logger.Info("Loading experience data...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM levels";
                command.Prepare();
                using (var sqliteDataReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                {
                    while (reader.Read())
                    {
                        var level = new ExperienceLevelTemplate();
                        level.Level = reader.GetByte("id");
                        level.TotalExp = reader.GetInt32("total_exp");
                        level.TotalMateExp = reader.GetInt32("total_mate_exp");
                        level.SkillPoints = reader.GetInt32("skill_points");
                        _levels.Add(level.Level, level);
                    }
                }
            }

            Logger.Info("Experience data loaded");
        }
    }
}
