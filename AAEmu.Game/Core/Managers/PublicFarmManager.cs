using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.PublicFarm;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class PublicFarmManager(ITaskManager taskManager, IWorldManager worldManager, ISubZoneManager subZoneManager) : Singleton<PublicFarmManager>, IPublicFarmManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, FarmGroupKind> _farmZones;

    public void Initialize()
    {
        Logger.Info("Initialising Public Farm Manager...");
        PublicFarmTickStart();
    }

    private void PublicFarmTickStart()
    {
        Logger.Info("PublicFarmTickTask: Started");

        var lpTickStartTask = new PublicFarmTickStartTask();
        taskManager.Schedule(lpTickStartTask, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public void PublicFarmTick()
    {
        // NOTE: Public farms only available in main_world
        var world = worldManager.GetWorld(WorldManager.DefaultInstanceId);
        var deleted = new List<Doodad>();
        foreach (var doodad in world.SpawnManager?.GetAllPlayerDoodads() ?? [])
        {
            if (doodad is null)
                continue;
            if (doodad.FarmType == FarmGroupKind.Invalid) { continue; }

            if (IsProtectedByPublicFarm(doodad)) { continue; }

            // defense time is up
            doodad.OwnerId = 0;
            doodad.OwnerType = DoodadOwnerType.System;
            doodad.FarmType = FarmGroupKind.Invalid;
            doodad.Save();
            deleted.Add(doodad);
        }

        foreach (var doodad in deleted)
        {
            //doodad.Delete();
            world.SpawnManager?.RemovePlayerDoodad(doodad);
        }
    }

    public bool InPublicFarm(WorldTemplate worldTemplate, Vector3 pos)
    {
        var subZoneList = subZoneManager.GetSubZoneByPosition(worldTemplate, pos);
        return subZoneList.Count > 0 && subZoneList.Any(subZoneId => _farmZones.ContainsKey(subZoneId));
    }

    private uint GetFarmSubZoneId(WorldInstance world, Vector3 pos)
    {
        var subZoneList = subZoneManager.GetSubZoneByPosition(world.Template, pos);

        return subZoneList.Count > 0 ? subZoneList.FirstOrDefault(subZoneId => _farmZones.ContainsKey(subZoneId)) : 0;
    }

    public FarmGroupKind GetFarmType(WorldInstance world, Vector3 pos)
    {
        var subZoneId = GetFarmSubZoneId(world, pos);
        return _farmZones.GetValueOrDefault(subZoneId, FarmGroupKind.Invalid);
    }

    /// <summary>
    /// Checks if a given doodad can be placed on a given farm type.
    /// Checks for type and max count
    /// </summary>
    /// <param name="character"></param>
    /// <param name="farmGroupKind"></param>
    /// <param name="doodadId"></param>
    /// <returns></returns>
    public bool CanPlace(Character character, FarmGroupKind farmGroupKind, uint doodadId)
    {
        var allPlanted = GetCommonFarmDoodads(character);
        if (allPlanted.TryGetValue(farmGroupKind, out var doodadList))
        {
            if (doodadList.Count >= CommonFarmGameData.Instance.GetFarmGroupMaxCount(farmGroupKind))
            {
                character.SendErrorMessage(Models.Game.ErrorMessageType.CommonFarmCountOver);
                return false;
            }
        }

        var allowedDoodads = CommonFarmGameData.Instance.GetAllowedDoodads(farmGroupKind);
        if (allowedDoodads.Any(id => doodadId == id))
        {
            return true;
        }

        character.SendErrorMessage(Models.Game.ErrorMessageType.CommonFarmNotAllowedType);
        return false;
    }

    public Dictionary<FarmGroupKind, List<Doodad>> GetCommonFarmDoodads(Character character)
    {
        var list = new Dictionary<FarmGroupKind, List<Doodad>>();

        var playerDoodads = character.ParentWorld.SpawnManager.GetPlayerDoodads(character.Id);

        foreach (var doodad in playerDoodads)
        {
            if (InPublicFarm(character.ParentWorld.Template, doodad.Transform.World.Position))
            {
                var farmType = GetFarmType(character.ParentWorld, doodad.Transform.World.Position);

                if (doodad.FarmType == farmType)
                {
                    if (!list.ContainsKey(farmType))
                        list.Add(farmType, []);
                    list[farmType].Add(doodad);
                }
            }
        }

        return list;
    }

    public static bool IsProtectedByPublicFarm(Doodad doodad)
    {
        var guardTime = CommonFarmGameData.Instance.GetFarmGuardTime(doodad.FarmType, doodad.Transform.ZoneId);
        if (guardTime == 0)
            return false;

        var protectionTime = doodad.PlantTime.AddMilliseconds(guardTime);

        return DateTime.UtcNow < protectionTime;
    }

    public void Load()
    {
        // Common farm subzone ID's
        // We have no idea where the client is actually pulling this data from
        _farmZones = new Dictionary<uint, FarmGroupKind>
        {
            { 966, FarmGroupKind.Farm },
            { 967, FarmGroupKind.Ranch },
            { 968, FarmGroupKind.Nursery },
            { 974, FarmGroupKind.Stable }, 
            { 998, FarmGroupKind.Farm },
        };
    }

}
