using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Менеджер локализации, загружающий переводы из таблицы <c>localized_texts</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class LocalizationManager : Singleton<LocalizationManager>, ILocalizationManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<string, string> _translations = [];

    private static string GetLookupKey(string tblName, string tblColumn, long index)
    {
        return $"{tblName}:{tblColumn}:{index}";
    }

    /// <summary>
    /// Загружает все записи из таблицы <c>localized_texts</c>.
    /// </summary>
    /// <remarks>
    /// Схема таблицы <c>localized_texts</c> (проверена по compact.sqlite3):
    /// <list type="bullet">
    ///   <item><description><c>tbl_name</c> text → имя таблицы для ключа</description></item>
    ///   <item><description><c>tbl_column_name</c> text → имя столбца для ключа</description></item>
    ///   <item><description><c>idx</c> int → индекс для ключа</description></item>
    ///   <item><description><c>en_us</c> text → значение перевода</description></item>
    /// </list>
    /// В текущей версии БД присутствует только столбец <c>en_us</c>, поэтому
    /// переводы всегда загружаются из него независимо от <see cref="AppConfiguration.Instance.DefaultLanguage"/>.
    /// </remarks>
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
                        AddTranslation(reader.GetString("tbl_name"), reader.GetString("tbl_column_name"), reader.GetInt64("idx"), reader.GetString("en_us"));
                }
            }
        }

        Logger.Info($"Loaded {_translations.Count} translations (source column: en_us, requested language: {AppConfiguration.Instance.DefaultLanguage}) ...");
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
