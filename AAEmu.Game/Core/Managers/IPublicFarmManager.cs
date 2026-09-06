using System.Numerics;

using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers;

public interface IPublicFarmManager : ILoadable, IInitializable
{
    void PublicFarmTick();
    bool InPublicFarm(WorldTemplate worldTemplate, Vector3 pos);
    FarmGroupKind GetFarmType(WorldInstance world, Vector3 pos);
}
