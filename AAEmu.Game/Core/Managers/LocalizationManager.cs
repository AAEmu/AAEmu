using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class LocalizationManager : Singleton<LocalizationManager>, ILocalizationManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<string, string> _translations = [];
    private Dictionary<string, Dictionary<long, string>> ItemNameCache { get; } = [];

    private static string GetLookupKey(string tblName, string tblColumn, long index)
    {
        return $"{tblName}:{tblColumn}:{index}";
    }

    public void Load()
    {
        Logger.Info("Loading translations ...");

        ItemNameCache.Clear();
        using (var connection = SQLite.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM localized_texts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    var columns = reader.GetColumnNames();
                    foreach (var column in columns)
                    {
                        if (column == "id" || column == "tbl_name" || column == "tbl_column_name" || column == "idx" || column.EndsWith("_ver"))
                            continue;
                        ItemNameCache.Add(column, []);
                    }
                    while (reader.Read())
                    {
                        var tblName = reader.GetString("tbl_name");
                        var columnName = reader.GetString("tbl_column_name");
                        var idx = reader.GetInt64("idx");
                        var defaultText = reader.GetString(AppConfiguration.Instance.DefaultLanguage);
                        AddTranslation(tblName, columnName, idx, defaultText);

                        // In case of item, create cache of all languages
                        if (tblName == "items" && columnName == "name")
                        {
                            foreach (var locale in ItemNameCache.Keys)
                            {
                                var translatedName = reader.GetString(locale)?.ToLower();
                                if (string.IsNullOrWhiteSpace(translatedName))
                                    continue;
                                var localeGroup = ItemNameCache.GetValueOrDefault(locale);
                                if (localeGroup == null)
                                    continue; // Should be impossible to reach
                                if (!localeGroup.TryAdd(idx, translatedName))
                                {
                                    localeGroup[idx] = translatedName;
                                }
                            }
                        }
                    }
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

    /// <summary>
    /// Checks if given keywords match a given item name in any of the locale available to the server
    /// </summary>
    /// <param name="itemTemplateId"></param>
    /// <param name="keywords"></param>
    /// <param name="exactMatch"></param>
    /// <returns></returns>
    public bool MatchItemName(long itemTemplateId, string keywords, bool exactMatch)
    {
        // Empty Check
        if (string.IsNullOrWhiteSpace(keywords))
            return false;

        // Lowercase
        var k = keywords.ToLower();

        // Split words for partial checks
        // I don't think this is retail behavior, but it makes search a lot better
        var searchWords = new List<string>();
        foreach (var word in k.Split(' '))
        {
            if (string.IsNullOrWhiteSpace(word))
                continue;
            searchWords.Add(word);
        }

        foreach (var (_, itemList) in ItemNameCache)
        {
            var localizedName = itemList.GetValueOrDefault(itemTemplateId);
            // Skip empty translations
            if (string.IsNullOrWhiteSpace(localizedName))
                continue;

            // Exact match check
            if (exactMatch)
            {
                if (localizedName == k)
                    return true;
                continue;
            }

            // Partial match check (with individual words)
            var matchAll = true;
            foreach (var searchWord in searchWords)
            {
                if (!localizedName.Contains(searchWord))
                {
                    matchAll = false;
                    break;
                }
            }

            if (matchAll)
            {
                return true;
            }
        }

        return false;
    }
}
