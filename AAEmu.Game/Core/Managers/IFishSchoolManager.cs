using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers;

public interface IFishSchoolManager : IInitializable
{
    void Load(WorldInstance world);
    void Track(Doodad doodad);
    void Untrack(Doodad doodad);
    List<Doodad> GetAllFishSchools();
}
