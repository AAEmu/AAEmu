using System.Collections.Generic;
using System.Text.RegularExpressions;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class NameManager : Singleton<NameManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Regex _characterNameRegex;
        private List<string> _characterNames;

        public NameManager()
        {
            _characterNames = new List<string>();
        }

        public void Load()
        {
            _characterNameRegex = new Regex(AppConfiguration.Instance.CharacterNameRegex, RegexOptions.Compiled);
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT name FROM characters";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            _characterNames.Add(reader.GetString(0).ToLower());
                    }
                }
            }

            _log.Info("Loaded {0} character names", _characterNames.Count);
        }

        public byte ValidationCharacterName(string name)
        {
            if (_characterNames.Contains(name))
                return 4; // Персонаж с таким именем уже существует. Выберите другое.
            if (name == "" || !_characterNameRegex.IsMatch(name)) // TODO ...
                return 5; // Это имя содержит недопустимую лексику.
            return 0;
        }

        public void AddCharacterName(string name)
        {
            _characterNames.Add(name);
        }

        public void RemoveCharacterName(string name)
        {
            _characterNames.Remove(name);
        }
    }
}