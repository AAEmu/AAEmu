using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class NameManager : Singleton<NameManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Regex _characterNameRegex;
    private Dictionary<uint, string> _characterNames;
    private Dictionary<uint, uint> _characterAccounts;

    public string GetCharacterName(uint characterId)
    {
        if (_characterNames.ContainsKey(characterId))
            return _characterNames[characterId].NormalizeName();
        return null;
    }

    public uint GetCharacterId(string characterName)
    {
        var res = (from x in _characterNames where (x.Value.ToLower() == characterName.ToLower()) select x.Key).FirstOrDefault(0u);
        return res;
    }

    public uint GetCharaterAccount(uint characterId)
    {
        if (_characterAccounts.TryGetValue(characterId, out var accountId))
            return accountId;
        return 0;
    }

    public NameManager()
    {
        _characterNames = new Dictionary<uint, string>();
        _characterAccounts = new Dictionary<uint, uint>();
    }

    public void Load()
    {
        _characterNameRegex = new Regex(AppConfiguration.Instance.CharacterNameRegex, RegexOptions.Compiled);
        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, name, account_id FROM characters";
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = reader.GetUInt32("id");
                        var name = reader.GetString("name").ToLower();
                        var account = reader.GetUInt32("account_id");
                        _characterNames.Add(id, name);
                        _characterAccounts.Add(id, account);
                    }
                }
            }
        }

        Logger.Info($"Loaded {_characterNames.Count} character names");
    }

    public CharacterCreateError ValidationCharacterName(string name)
    {
        if (_characterNames.ContainsValue(name))
        {
            if (CharacterManager.Instance.IsCharacterPendingDeletion(name))
                return CharacterCreateError.Failed;

            return CharacterCreateError.NameAlreadyExists;
        }

        if (string.IsNullOrWhiteSpace(name) || !_characterNameRegex.IsMatch(name))
            return CharacterCreateError.InvalidCharacters;

        return CharacterCreateError.Ok;
    }

    public void AddCharacterName(uint characterId, string name, uint accountId)
    {
        if (!_characterNames.TryAdd(characterId, name))
        {
            var oldName = _characterNames.GetValueOrDefault(characterId) ?? string.Empty;
            if (string.Compare(name, oldName, StringComparison.InvariantCultureIgnoreCase) != 0)
                Logger.Error($"AddCharacterName, failed to register name for {name} ({characterId}), Account {accountId}, OldName {oldName}");
        }
        else
        {
            Logger.Info($"AddCharacterName, Registered character name {name} ({characterId})");
        }

        if (!_characterAccounts.TryAdd(characterId, accountId))
        {
            var oldAccount = _characterAccounts.GetValueOrDefault(characterId);
            if (accountId != oldAccount)
                Logger.Error($"AddCharacterName, failed to register account for {name} ({characterId}), Account {accountId}, OldAccount {oldAccount}");
        }
        else
        {
            Logger.Info($"AddCharacterName, Registered account {accountId} for {name} ({characterId})");
        }
    }

    public void RemoveCharacterName(uint characterId)
    {
        if (_characterNames.ContainsKey(characterId))
        {
            _characterNames.Remove(characterId);
            Logger.Info($"AddCharacterName, Remove name registration for character Id {characterId}");
        }
        else
        {
            Logger.Error($"AddCharacterName, No name was registered for character Id {characterId}");
        }

        if (_characterAccounts.ContainsKey(characterId))
        {
            _characterAccounts.Remove(characterId);
            Logger.Info($"AddCharacterName, Removed account registration for character Id {characterId}");
        }
        else
        {
            Logger.Error($"AddCharacterName, No account was registered for character Id {characterId}");
        }
    }

    public bool NoNamesRegistered()
    {
        return _characterNames.Count <= 0;
    }
}
