using System.Numerics;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers;

public interface IPublicFarmManager : ILoadable, IInitializable
{
    void PublicFarmTick();
    bool InPublicFarm(WorldTemplate worldTemplate, Vector3 pos);
    FarmType GetFarmType(WorldInstance world, Vector3 pos);
    void GetFarmArea(WorldInstance world, Vector3 pos, out FarmType farmType, out List<Vector3> positions);
    bool RemoveCrop(Doodad doodad, Character caller);
}
