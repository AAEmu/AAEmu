using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{

    public class LocalizationManager : Singleton<LocalizationManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<string, string> _translations;
        /// <summary>
        /// If you want Russian as default server language, use "ru" here instead of "en_us"
        /// </summary>
        private static string DefaultLanguage = "en_us"; // TODO: Add this to config


        public LocalizationManager()
        {
            _translations = new Dictionary<string, string>();
        }

        private string GetLookupKey(string tbl_name, string tbl_column, long index)
        {
            return string.Format("{0}:{1}:{2}", tbl_name, tbl_column, index);
        }

        public void Load()
        {
            _log.Info("Loading translations ...", _translations.Count);

            using (var connection = SQLite.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM localized_texts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                            AddTranslation(reader.GetString("tbl_name"), reader.GetString("tbl_column_name"), reader.GetInt64("idx"), reader.GetString(DefaultLanguage));
                    }
                }
            }

            _log.Info("Loaded {0} translations ...", _translations.Count);
        }

        public void AddTranslation(string tbl_name, string tbl_column, long index, string translationValue)
        {
            if (!_translations.TryAdd(GetLookupKey(tbl_name, tbl_column, index), translationValue))
                _log.Error("Failed to add translation: {0}:{1}:{2}", tbl_name, tbl_column, index);
        }

        public string Get(string tbl_name, string tbl_column, long index, string fallbackValue = "")
        {
            var key = GetLookupKey(tbl_name, tbl_column, index);
            if (_translations.TryGetValue(key, out var translatedText))
            {
                if (translatedText == string.Empty)
                    return fallbackValue;
                else
                    return translatedText;
            }
            else
            {
                return fallbackValue;
            }
        }

    }
}
