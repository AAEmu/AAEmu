using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IButlerManager
{
    CharacterButler GetOrCreate(uint characterId);
    IReadOnlyList<CharacterButler> SnapshotAll();
    ButlerOperationResult Bind(Character character, House house, Func<uint, House> registeredHouseResolver,
        bool notifyOwner = false);
    ButlerOperationResult Unbind(Character character, bool notifyOwner = false);
    bool UnbindHouse(uint houseId, bool notifyOwner = true);
    void RemoveCharacter(uint characterId);
    ButlerPresentation GetPresentation(Character character);
    ButlerPresentation GetPresentation(Character character, Action<ButlerPresentation> publish);
    bool IsHouseBound(uint houseId);
    void Save(CharacterButler butler, MySqlConnection connection, MySqlTransaction transaction);
}

public readonly record struct ButlerOperationResult(bool Success, ErrorMessageType Error);

public readonly record struct ButlerPresentation(
    bool IsBound,
    ButlerInfoWire Info,
    string HouseName,
    ErrorMessageType Error = ErrorMessageType.NoErrorMessage);
