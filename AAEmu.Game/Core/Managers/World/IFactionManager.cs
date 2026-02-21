using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Managers.World;

public interface IFactionManager
{
    void Load();
    SystemFaction GetFaction(FactionsEnum id);
    void AddFaction(SystemFaction faction);
}
