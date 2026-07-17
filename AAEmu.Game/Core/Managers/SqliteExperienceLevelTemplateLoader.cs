#nullable enable

using AAEmu.Game.Models.Game;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Loads experience level templates from a SQLite database.
/// </summary>
/// <remarks>
/// Reads from the <c>levels</c> table in <c>compact.sqlite3</c>.
/// Schema: id (PK), expedition_exp, req_item_count, req_item_id, skill_points, total_exp, total_mate_exp.
/// </remarks>
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
            levelTemplate.ExpeditionExp = reader.GetInt32("expedition_exp");
            levelTemplate.ReqItemCount = reader.GetInt32("req_item_count");
            levelTemplate.ReqItemId = reader.GetInt32("req_item_id");
            levelTemplate.SkillPoints = reader.GetInt32("skill_points");
            levelTemplate.TotalExp = reader.GetInt32("total_exp");
            levelTemplate.TotalMateExp = reader.GetInt32("total_mate_exp");

            if (levelTemplate.Level != expectedLevel)
            {
                logger.Warn("Experience data is missing level {0}", expectedLevel);
            }

            if (levelTemplate.TotalExp <= lastExp)
            {
                logger.Warn("Experience data is not sorted by total_exp");
            }

            if (levelTemplate.TotalMateExp <= lastMateExp)
            {
                logger.Warn("Experience data is not sorted by total_mate_exp");
            }

            yield return levelTemplate;

            expectedLevel++;
            lastExp = levelTemplate.TotalExp;
            lastMateExp = levelTemplate.TotalMateExp;
        }
    }
}
