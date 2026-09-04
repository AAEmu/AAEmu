using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class FishSchoolManager : Singleton<FishSchoolManager>, IFishSchoolManager
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
        Logger.Info("Loading FishSchool...");
        var fishSchool = new List<Doodad>();
        var doodads = world?.GetAllDoodads();
        if (doodads != null)
        {
            foreach (var d in doodads)
            {
                if (FishSchoolLookup.IsSchool(d))
                    fishSchool.Add(d);
            }
        }

        lock (FishSchools)
        {
            var worldId = world?.Id ?? 0;
            FishSchools[worldId] = fishSchool;
        }

        Logger.Info($"Loaded {fishSchool.Count} FishSchool for world {world} ...");
    }

    public void Track(Doodad doodad)
    {
        if (!FishSchoolLookup.IsSchool(doodad))
            return;

        var worldId = doodad.ParentWorld?.Id ?? 0;
        lock (FishSchools)
        {
            if (!FishSchools.TryGetValue(worldId, out var worldFishList))
            {
                worldFishList = [];
                FishSchools[worldId] = worldFishList;
            }

            if (!worldFishList.Contains(doodad))
                worldFishList.Add(doodad);
        }
    }

    public void Untrack(Doodad doodad)
    {
        if (doodad == null)
            return;

        lock (FishSchools)
        {
            foreach (var worldFishList in FishSchools.Values)
                worldFishList.Remove(doodad);
        }
    }

    public List<Doodad> GetAllFishSchools()
    {
        var res = new List<Doodad>();
        lock (FishSchools)
        {
            foreach (var doodads in FishSchools.Values)
            {
                foreach (var doodad in doodads)
                {
                    if (FishSchoolLookup.IsPresent(doodad))
                        res.Add(doodad);
                }
            }
        }

        return res;
    }
}
