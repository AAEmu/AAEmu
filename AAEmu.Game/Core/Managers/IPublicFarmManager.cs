using System.Numerics;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers;

public interface IPublicFarmManager : ILoadable, IInitializable
{
    void PublicFarmTick();
    bool InPublicFarm(WorldTemplate worldTemplate, Vector3 pos);
    FarmType GetFarmType(WorldInstance world, Vector3 pos);

    /// <summary>How many farm doodads of one tab this character currently has planted.</summary>
    uint GetPlantedCount(Character character, FarmType farmType);

    /// <summary>Removes every farm doodad this character has planted, returning how many went away.</summary>
    int RemoveCharacterFarms(Character character);
}
