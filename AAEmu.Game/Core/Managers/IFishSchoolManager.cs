using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers;

public interface IFishSchoolManager
{
    void Initialize();
    void Load(WorldInstance world);
    List<Doodad> GetAllFishSchools();
}
