using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Managers;

public interface INameManager : ILoadable
{
    string GetCharacterName(uint characterId);
    uint GetCharacterId(string normalizedCharacterName);
    uint GetCharacterAccount(uint characterId);
    CharacterCreateError ValidateCharacterName(string name);
    void AddCharacter(uint characterId, string name, uint accountId);
    void RemoveCharacterId(uint characterId);
    bool NoNamesRegistered();
}
