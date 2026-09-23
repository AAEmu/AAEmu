using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.InstantGame.Static;

namespace AAEmu.Game.Core.Managers;

public interface IInstantGameManager : IInitializable
{
    void ApplyToBattlefield(uint battlefieldId, InstantCorps corps, Character character);
    void WithdrawFromBattlefield(Character character);

    /// <summary>Releases every queue slot and per-player match state a disconnecting character holds.</summary>
    void OnCharacterLogout(Character character);
}
