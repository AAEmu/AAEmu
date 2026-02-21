using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Shipyard;

namespace AAEmu.Game.Core.Managers;

public interface IShipyardManager
{
    void Initialize();
    Shipyard Create(Character owner, ShipyardData shipyardData);
}
