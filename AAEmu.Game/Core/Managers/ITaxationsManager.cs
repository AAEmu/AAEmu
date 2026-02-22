using AAEmu.Game.Models.Game.Taxations;

namespace AAEmu.Game.Core.Managers;

public interface ITaxationsManager : ILoadable
{
    Dictionary<uint, Taxation> Taxations { get; }
}
