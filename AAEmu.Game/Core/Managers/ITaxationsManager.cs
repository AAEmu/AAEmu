using AAEmu.Game.Models.Game.Taxations;

namespace AAEmu.Game.Core.Managers;

public interface ITaxationsManager
{
    Dictionary<uint, Taxation> Taxations { get; }
    void Load();
}
