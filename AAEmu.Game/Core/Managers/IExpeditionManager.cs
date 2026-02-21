using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Core.Managers;

public interface IExpeditionManager
{
    IEnumerable<Expedition> Expeditions { get; }
    void Load();
}
