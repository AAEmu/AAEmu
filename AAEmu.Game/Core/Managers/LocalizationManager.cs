using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class LocalizationManager : Singleton<LocalizationManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<string, string> _translations = [];

    private static string GetLookupKey(string tblName, string tblColumn, long index)
    {
        return $"{tblName}:{tblColumn}:{index}";
    }

    public void Load()
    {
        Logger.Info("Loading translations ...");

        using (var connection = SQLite.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM localized_texts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                        AddTranslation(reader.GetString("tbl_name"), reader.GetString("tbl_column_name"), reader.GetInt64("idx"), reader.GetString(AppConfiguration.Instance.DefaultLanguage));
                }
            }
        }

        Logger.Info($"Loaded {_translations.Count} translations in {AppConfiguration.Instance.DefaultLanguage} ...");
    }

    public void AddTranslation(string tblName, string tblColumn, long index, string translationValue)
    {
        if (!_translations.TryAdd(GetLookupKey(tblName, tblColumn, index), translationValue))
            Logger.Error($"Failed to add translation: {tblName}:{tblColumn}:{index}");
    }

    public string Get(string tblName, string tblColumn, long index, string fallbackValue = "")
    {
        var key = GetLookupKey(tblName, tblColumn, index);
        if (_translations.TryGetValue(key, out var translatedText))
        {
            return translatedText == string.Empty ? fallbackValue : translatedText;
        }

        return fallbackValue;
    }
}
