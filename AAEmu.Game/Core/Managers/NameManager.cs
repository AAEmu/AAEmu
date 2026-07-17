using System.Text.RegularExpressions;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models;
using AAEmu.Game.Models.StaticValues;
using Microsoft.Extensions.Options;
using NLog;

namespace AAEmu.Game.Core.Managers;

public partial class NameManager(Lazy<ICharacterManager> characterManager = null, IOptions<AppConfiguration> options = null) : Singleton<NameManager>, INameManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private Regex _characterNameRegex;
    private Dictionary<uint, string> _characterIds = [];
    private Dictionary<string, uint> _characterNames = [];
    private Dictionary<uint, ulong> _characterAccounts = [];

    public string GetCharacterName(uint characterId)
        => _characterIds.TryGetValue(characterId, out var characterName)
        ? characterName
        : null;

    public uint GetCharacterId(string normalizedCharacterName)
        => _characterNames.TryGetValue(normalizedCharacterName, out var characterId)
        ? characterId
        : 0u;

    public ulong GetCharacterAccount(uint characterId)
        => _characterAccounts.TryGetValue(characterId, out var accountId)
        ? accountId
        : 0UL;

    public NameManager() : this(null, null) { }

    private const string DefaultCharacterNameRegexPattern = "^[a-zA-Z0-9а-яА-Я]{1,18}$";
    [GeneratedRegex(DefaultCharacterNameRegexPattern)]
    private static partial Regex DefaultCharacterNameRegex();

    public void Load()
    {
        if (options?.Value.CharacterNameRegex is { } characterNameRegex &&
            characterNameRegex != DefaultCharacterNameRegexPattern)
        {
            _characterNameRegex = new Regex(characterNameRegex, RegexOptions.Compiled);
        }

        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, name, account_id, deleted FROM characters";
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = reader.GetUInt32("id");
                        var name = reader.GetString("name").ToLower();
                        var account = reader.GetUInt64("account_id");
                        var deleted = reader.GetInt32("deleted");
                        var normalizedName = name.NormalizeName();
                        _characterIds.Add(id, normalizedName);
                        if (deleted == 0)
                            _characterNames.Add(normalizedName, id); // Ignore deleted names, but do add the IDs to the old account
                        _characterAccounts.Add(id, account);
                    }
                }
            }
        }

        Logger.Info($"Loaded {_characterIds.Count} character names");
    }

    /// <summary>
    /// For testing purposes
    /// </summary>
    /// <param name="characterIds">Initial character ids</param>
    /// <param name="characterNames">Initial character names</param>
    /// <param name="characterAccounts">Initial character accounts</param>
    internal void Load(
        Dictionary<uint, string> characterIds,
        Dictionary<string, uint> characterNames,
        Dictionary<uint, ulong> characterAccounts)
    {
        if (options?.Value.CharacterNameRegex is { } characterNameRegex &&
            characterNameRegex != DefaultCharacterNameRegexPattern)
        {
            _characterNameRegex = new Regex(characterNameRegex, RegexOptions.Compiled);
        }

        _characterIds = characterIds;
        _characterNames = characterNames;
        _characterAccounts = characterAccounts;
    }

    public CharacterCreateError ValidateCharacterName(string name)
    {
        if (_characterNames.TryGetValue(name, out var existingId))
        {
            if (characterManager?.Value.IsCharacterPendingDeletion(name) == true)
                return CharacterCreateError.Failed;

            return CharacterCreateError.NameAlreadyExists;
        }

        if (string.IsNullOrWhiteSpace(name) || !ValidatesName(name.AsSpan()))
            return CharacterCreateError.InvalidCharacters;

        return CharacterCreateError.Ok;
    }

    private bool ValidatesName(ReadOnlySpan<char> name) =>
        (_characterNameRegex ?? DefaultCharacterNameRegex())
        .IsMatch(name);

    public void AddCharacter(uint characterId, string name, ulong accountId)
    {
        var normalizedName = name.NormalizeName();
        if (!_characterIds.TryAdd(characterId, name.NormalizeName()))
        {
            var oldName = _characterIds.GetValueOrDefault(characterId) ?? string.Empty;
            if (string.Compare(name, oldName, StringComparison.InvariantCultureIgnoreCase) != 0)
                Logger.Error($"AddCharacterName, failed to register name for {name} ({characterId}), Account {accountId}, OldName {oldName}");
        }
        else
        {
            Logger.Info($"AddCharacterName, Registered character name {name} ({characterId})");
        }

        if (!_characterNames.TryAdd(normalizedName, characterId))
        {
            var oldId = _characterNames.GetValueOrDefault(normalizedName);
            if (characterId != oldId)
                Logger.Error($"AddCharacterName, failed to register id for {name} ({characterId}), Account {accountId}, OldId {oldId}");
        }
        else
        {
            Logger.Info($"AddCharacterName, Registered character id {name} ({characterId})");
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

    public void RemoveCharacterId(uint characterId)
    {
        if (_characterIds.TryGetValue(characterId, out var characterName))
        {
            _characterIds.Remove(characterId);
            _characterNames.Remove(characterName);
            Logger.Info($"AddCharacterName, Remove name and id registrations for character Id {characterId}");
        }
        else
        {
            Logger.Error($"AddCharacterName, No name was registered for character Id {characterId}");
        }

        if (_characterAccounts.Remove(characterId))
        {
            Logger.Info($"AddCharacterName, Removed account registration for character Id {characterId}");
        }
        else
        {
            Logger.Error($"AddCharacterName, No account was registered for character Id {characterId}");
        }
    }

    public bool NoNamesRegistered()
    {
        return _characterIds.Count <= 0;
    }
}
