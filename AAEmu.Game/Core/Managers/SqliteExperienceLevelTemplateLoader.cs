#nullable enable

using AAEmu.Game.Models.Game;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Loads experience level templates from a SQLite database.
/// </summary>
public sealed class SqliteExperienceLevelTemplateLoader(ILogger logger) : IExperienceLevelTemplateLoader
{
    public IEnumerable<ExperienceLevelTemplate> Load()
    {
        using var connection = SQLite.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM levels ORDER BY id ASC";
        command.Prepare();
        using var sqliteDataReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteDataReader);

        // Validate the data, must start at level 1, and have increasing experience
        var expectedLevel = 1;
        var lastExp = -1;
        var lastMateExp = -1;
        while (reader.Read())
        {
            var levelTemplate = new ExperienceLevelTemplate();
            levelTemplate.Level = reader.GetByte("id");
            levelTemplate.TotalExp = reader.GetInt32("total_exp");
            levelTemplate.TotalMateExp = reader.GetInt32("total_mate_exp");
            levelTemplate.SkillPoints = reader.GetInt32("skill_points");

            if (levelTemplate.Level != expectedLevel)
            {
                logger.Error("Experience data is missing level {0}", expectedLevel);
                throw new InvalidDataException($"Experience data is missing level {expectedLevel}");
            }

            if (levelTemplate.TotalExp <= lastExp)
            {
                logger.Error("Experience data is not sorted by total_exp");
                throw new InvalidDataException("Experience data is not sorted by total_exp");
            }

            if (levelTemplate.TotalMateExp <= lastMateExp)
            {
                logger.Error("Experience data is not sorted by total_mate_exp");
                throw new InvalidDataException("Experience data is not sorted by total_mate_exp");
            }

            yield return levelTemplate;

            expectedLevel++;
            lastExp = levelTemplate.TotalExp;
            lastMateExp = levelTemplate.TotalMateExp;
        }
    }
}
