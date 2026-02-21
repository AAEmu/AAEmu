using System.Numerics;

using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers;

public interface IPublicFarmManager
{
    void Initialize();
    void PublicFarmTick();
    bool InPublicFarm(WorldTemplate worldTemplate, Vector3 pos);
    FarmType GetFarmType(WorldInstance world, Vector3 pos);
}
