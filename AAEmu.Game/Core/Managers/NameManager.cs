using System.Collections.Generic;
using System.Linq;
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
        private Dictionary<uint, string> _characterNames;

        public string GetCharacterName(uint characterId)
        {
            if (_characterNames.ContainsKey(characterId))
                return _characterNames[characterId].FirstCharToUpper();
            return null;
        }

        public uint GetCharacterId(string characterName)
        {
            var res = (from x in _characterNames where (x.Value.ToLower() == characterName.ToLower()) select x.Key).FirstOrDefault();
            return res ;
        }

        public NameManager()
        {
            _characterNames = new Dictionary<uint, string>();
        }

        public void Load()
        {
            _characterNameRegex = new Regex(AppConfiguration.Instance.CharacterNameRegex, RegexOptions.Compiled);
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT id, name FROM characters";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            _characterNames.Add(reader.GetUInt32("id"), reader.GetString("name").ToLower());
                    }
                }
            }

            _log.Info("Loaded {0} character names", _characterNames.Count);
        }

        public byte ValidationCharacterName(string name)
        {
            if (_characterNames.Values.Contains(name))
                return 4; // Персонаж с таким именем уже существует. Выберите другое.
            if (name == "" || !_characterNameRegex.IsMatch(name)) // TODO ...
                return 5; // Это имя содержит недопустимую лексику.
            return 0;
        }

        public void AddCharacterName(uint characterId, string name)
        {
            _characterNames.Add(characterId, name);
        }

        public void RemoveCharacterName(uint characterId)
        {
            _characterNames.Remove(characterId);
        }
    }
}
