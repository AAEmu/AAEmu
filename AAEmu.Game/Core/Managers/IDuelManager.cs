using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Duels;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Managers;

public interface IDuelManager : IInitializable
{
    Dictionary<uint, FactionsEnum> SaveFactions { get; set; }
    void DuelRequest(Character challenger, uint challengedId, byte duelType = 0);
    void DuelRequestExpired(uint challengerId);
    void OnCharacterLogout(Character character);
    void DuelAccepted(Character challenged, uint challengerId);
    void DuelStart(uint id);
    void DuelCancel(uint challengerId, ErrorMessageType errorMessage);
    void DuelStop(uint id, DuelDetType det, uint loseId = 0);
    bool DuelResultСheck(uint id);
    DuelDistance DuelDistanceСheck(uint id);
}
