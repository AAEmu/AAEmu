using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Managers;

public interface ITradeManager
{
    void CanStartTrade(Character owner, Character target);
    void StartTrade(Character owner, Character target);
}
