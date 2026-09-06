using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class LocalizationManager : Singleton<LocalizationManager>, ILocalizationManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<string, string> _translations = [];
    private readonly Dictionary<string, List<string>> _allTranslations = [];

    private static string GetLookupKey(string tblName, string tblColumn, long index)
    {
        return $"{tblName}:{tblColumn}:{index}";
    }

    public void Load()
    {
        Logger.Info("Loading translations ...");

        using (var connection = SQLite.CreateConnection())
        {
            var languageColumns = ReadLanguageColumns(connection);
            if (languageColumns.Count == 0)
                languageColumns.Add(AppConfiguration.Instance.DefaultLanguage);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM localized_texts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var tblName = reader.GetString("tbl_name");
                        var tblColumn = reader.GetString("tbl_column_name");
                        var index = reader.GetInt64("idx");
                        var display = ReadDisplay(reader, languageColumns);
                        var names = ReadLanguageValues(reader, languageColumns);

                        AddTranslation(tblName, tblColumn, index, display);
                        AddTranslations(tblName, tblColumn, index, names);
                    }
                }
            }

            Logger.Info($"Loaded {_translations.Count} translations across {languageColumns.Count} language columns (display={AppConfiguration.Instance.DefaultLanguage}) ...");
        }
    }

    public void AddTranslation(string tblName, string tblColumn, long index, string translationValue)
    {
        var key = GetLookupKey(tblName, tblColumn, index);
        if (!_translations.TryAdd(key, translationValue ?? string.Empty))
        {
            Logger.Error($"Failed to add translation: {tblName}:{tblColumn}:{index}");
            return;
        }

        AppendNames(key, [translationValue]);
    }

    public void AddTranslations(string tblName, string tblColumn, long index, IEnumerable<string> translationValues)
    {
        AppendNames(GetLookupKey(tblName, tblColumn, index), translationValues);
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

    public IReadOnlyList<string> GetAll(string tblName, string tblColumn, long index)
    {
        return _allTranslations.TryGetValue(GetLookupKey(tblName, tblColumn, index), out var names)
            ? names
            : [];
    }

    private void AppendNames(string key, IEnumerable<string> values)
    {
        var incoming = LocalizedTextSearchRules.UniqueNames(values);
        if (incoming.Count == 0)
            return;

        if (!_allTranslations.TryGetValue(key, out var existing))
        {
            _allTranslations[key] = incoming;
            return;
        }

        _allTranslations[key] = LocalizedTextSearchRules.UniqueNames(existing.Concat(incoming));
    }

    private static string ReadDisplay(SQLiteWrapperReader reader, IReadOnlyList<string> languageColumns)
    {
        var defaultLanguage = AppConfiguration.Instance.DefaultLanguage;
        return HasLanguage(languageColumns, defaultLanguage)
            ? reader.GetString(defaultLanguage, string.Empty)
            : string.Empty;
    }

    private static List<string> ReadLanguageValues(SQLiteWrapperReader reader, IReadOnlyList<string> languageColumns)
    {
        var values = new List<string>(languageColumns.Count);
        foreach (var column in languageColumns)
            values.Add(reader.GetString(column, string.Empty));
        return values;
    }

    private static bool HasLanguage(IReadOnlyList<string> languageColumns, string language)
    {
        if (string.IsNullOrEmpty(language))
            return false;

        foreach (var column in languageColumns)
        {
            if (column.Equals(language, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static List<string> ReadLanguageColumns(SqliteConnection connection)
    {
        var tableColumns = new List<string>();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(localized_texts)";
        using var reader = pragma.ExecuteReader();
        while (reader.Read())
            tableColumns.Add(reader.GetString(1));

        return LocalizedTextSearchRules.LanguageColumns(tableColumns);
    }
}
