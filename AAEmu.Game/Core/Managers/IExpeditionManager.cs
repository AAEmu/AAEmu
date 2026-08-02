using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Managers;

public interface IExpeditionManager : ILoadable
{
    IEnumerable<Expedition> Expeditions { get; }
    bool TryChangeContributionPoints(Character character, int amount, bool addToWeeklyTotal);
}
