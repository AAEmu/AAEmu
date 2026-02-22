using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.InstantGame.Static;

namespace AAEmu.Game.Core.Managers;

public interface IInstantGameManager : IInitializable
{
    void ApplyToBattlefield(uint battlefieldId, InstantCorps corps, Character character);
    void WithdrawFromBattlefield(Character character);
}
