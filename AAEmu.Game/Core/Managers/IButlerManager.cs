using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IButlerManager
{
    CharacterButler GetOrCreate(uint characterId);
    ButlerOperationResult Bind(Character character, House house, Func<uint, House> registeredHouseResolver);
    ButlerOperationResult Unbind(Character character);
    bool UnbindHouse(uint houseId, bool notifyOwner = true);
    void RemoveCharacter(uint characterId);
    ButlerPresentation GetPresentation(Character character);
    bool IsHouseBound(uint houseId);
    void Save(CharacterButler butler, MySqlConnection connection, MySqlTransaction transaction);
}

public readonly record struct ButlerOperationResult(bool Success, ErrorMessageType Error);

public readonly record struct ButlerPresentation(bool IsBound, ButlerInfoWire Info, string HouseName);
