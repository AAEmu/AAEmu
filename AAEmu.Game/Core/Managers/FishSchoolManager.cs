using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class FishSchoolManager : Singleton<FishSchoolManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    /// <summary>
    /// Collection of Fish school doodads (worldInstanceId, List of doodads)
    /// </summary>
    private Dictionary<uint, List<Doodad>> FishSchools { get; set; } = [];

    public void Initialize()
    {
        FishSchools = [];
        Logger.Info("Initialising FishSchool Manager...");
    }

    public void Load(WorldInstance world)
    {
        var fishSchool = new List<Doodad>();
        Logger.Info("Loading FishSchool...");
        var doodads = world.GetAllDoodads();
        if (doodads != null)
        {
            foreach (var d in doodads)
            {
                // ID=6447, "Freshwater Fish School", ID=6448, "Saltwater Fish School"
                if ((d.TemplateId == DoodadConstants.FreshwaterFishSchool) || (d.TemplateId == DoodadConstants.SaltwaterFishSchool))
                    fishSchool.Add(d);
            }

            lock (FishSchools)
            {
                if (fishSchool.Count > 0)
                {
                    if (!FishSchools.TryGetValue(world.Id, out var worldFishList))
                    {
                        worldFishList = [];
                        FishSchools.Add(world.Id, worldFishList);
                    }

                    worldFishList.AddRange(fishSchool);
                }
            }
        }
        Logger.Info($"Loaded {fishSchool.Count} FishSchool for world {world} ...");
    }

    public List<Doodad> GetAllFishSchools()
    {
        var res = new List<Doodad>();
        foreach (var (world, doodads) in FishSchools)
        {
            res.AddRange(doodads);
        }
        return res;
    }
}
