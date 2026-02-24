using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Managers;

public interface IExpeditionManager : ILoadable
{
    IEnumerable<Expedition> Expeditions { get; }
}
