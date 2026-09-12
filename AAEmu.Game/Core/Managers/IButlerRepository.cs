using AAEmu.Game.Models.Game.Butlers;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IButlerRepository
{
    IReadOnlyList<CharacterButlerRecord> LoadAll();
    bool TryChangeHouse(CharacterButlerRecord record, uint expectedHouseId);
    void Save(CharacterButlerRecord record, MySqlConnection connection, MySqlTransaction transaction);
    void Delete(uint characterId);
}
