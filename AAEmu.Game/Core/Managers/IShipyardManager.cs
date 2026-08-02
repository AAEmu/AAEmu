using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Shipyard;

namespace AAEmu.Game.Core.Managers;

public interface IShipyardManager : ILoadable, IInitializable
{
    Shipyard Create(Character owner, ShipyardData shipyardData, bool useAaPoint);
    void ShipyardCompletedTask(Shipyard shipyard);
}
