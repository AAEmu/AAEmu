using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.FishSchools;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class FishSchoolManager : Singleton<FishSchoolManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private const double Delay = 500;
    private Dictionary<uint, List<Doodad>> FishSchools { get; set; } = new();

    public void Initialize()
    {
        FishSchools = new Dictionary<uint, List<Doodad>>();
        Logger.Info("Initialising FishSchool Manager...");
    }

    public void Load(uint worldId)
    {
        var fishSchool = new List<Doodad>();
        Logger.Info("Loading FishSchool...");
        var doodads = WorldManager.Instance.GetAllDoodads();
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
                    if (!FishSchools.TryGetValue(worldId, out var worldFishList))
                    {
                        worldFishList = new List<Doodad>();
                        FishSchools.Add(worldId, worldFishList);
                    }

                    worldFishList.AddRange(fishSchool);
                }
            }
        }
        Logger.Info($"Loaded {fishSchool.Count} FishSchool for worldId={worldId}...");
    }

    public static void FishFinderStart(Character character)
    {
        if (character.FishSchool.FishFinderTickTask != null)
        {
            StopFishFinderTickAsync(character).GetAwaiter().GetResult();
            return;
        }

        character.SendPacket(new SCSchoolOfFishFinderToggledPacket(true, 800));
        character.FishSchool.FishFinderTickTask = new FishSchoolTickTask(character);
        TaskManager.Instance.Schedule(character.FishSchool.FishFinderTickTask, TimeSpan.FromMilliseconds(Delay));
    }

    internal void FishFinderTick(Character character)
    {
        const int MaxCount = 10;
        Doodad[] transfers;

        // не ограничивать дальность видимости для GM & Admins
        if (character.AccessLevel == 0)
        {
            var transfers2 = new List<Doodad>();
            foreach (var t in FishSchools[character.Transform.WorldId])
            {
                if (!(MathF.Abs(MathUtil.CalculateDistance(character, t)) < 800f)) { continue; }

                transfers2.Add(t);
            }
            transfers = transfers2.ToArray();
        }
        else
        {
            transfers = FishSchools[character.Transform.WorldId].ToArray();
        }

        if (transfers.Length > 0)
        {
            for (var i = 0; i < transfers.Length; i += MaxCount)
            {
                var last = transfers.Length - i <= MaxCount;
                var temp = new Doodad[last ? transfers.Length - i : MaxCount];
                Array.Copy(transfers, i, temp, 0, temp.Length);
                character.SendPacket(new SCSchoolOfFishDoodadsPacket(last, temp));
            }
        }
        else
        {
            character.SendPacket(new SCSchoolOfFishDoodadsPacket(true, Array.Empty<Doodad>()));
        }
        TaskManager.Instance.Schedule(character.FishSchool.FishFinderTickTask, TimeSpan.FromMilliseconds(Delay));
    }

    public static async System.Threading.Tasks.Task StopFishFinderTickAsync(Character character)
    {
        if (character.FishSchool.FishFinderTickTask == null)
            return;

        await character.FishSchool.FishFinderTickTask.CancelAsync();
        character.FishSchool.FishFinderTickTask = null;
        character.SendPacket(new SCSchoolOfFishFinderToggledPacket(false, 0));
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
