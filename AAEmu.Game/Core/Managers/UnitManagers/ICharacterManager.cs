using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Char.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers.UnitManagers;

public interface ICharacterManager : ILoadable
{
    CharacterTemplate GetTemplate(Race race, Gender gender);
    bool IsCreatable(Race race, Gender gender);
    AppellationTemplate GetAppellationsTemplate(uint id);
    List<Expand> GetExpands(int step);
    ActabilityTemplate GetActability(uint id);
    uint GetActabilityIdByCategoryId(uint id);
    ExpertLimit GetExpertLimit(int step);
    ExpertLimit GetLanguageExpertLimit(int step);
    ExpertLimit GetPointCapLimit(uint actabilityId, int step);
    ExpandExpertLimit GetExpandExpertLimit(int step);
    int GetEffectiveAccessLevel(Character character);
    void Create(GameConnection connection, string name, Race race, Gender gender, uint[] bodyItems, UnitCustomModelParams customModel, AbilityType ability1, AbilityType ability2, AbilityType ability3, byte level);
    bool IsCharacterPendingDeletion(string name);
    void StartOnlineTracking();
    void PlayerRoll(Character player, int max);
    void DeleteCharacterAssets(Character character);
    bool CheckForDeletedCharactersDeletion(Character character, GameConnection gameConnection, MySqlConnection dbConnection);
    void CheckForDeletedCharacters();
    void SetDeleteCharacter(GameConnection gameConnection, uint characterId);
    void SetRestoreCharacter(GameConnection gameConnection, uint characterId);
}
