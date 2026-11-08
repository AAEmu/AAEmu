using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.World;
using Jitter2.LinearMath;

namespace AAEmu.Game.Core.Managers;

public interface IPublicFarmManager : ILoadable, IInitializable
{
    void PublicFarmTick();
    bool InPublicFarm(WorldTemplate worldTemplate, JVector pos);
    FarmGroupKind GetFarmType(WorldInstance world, JVector pos);
}
