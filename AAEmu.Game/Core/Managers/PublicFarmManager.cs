using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
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

    private Dictionary<uint, FarmType> _farmZones;

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
            if (doodad.FarmType == FarmType.Invalid) { continue; }
            var guardTime = CommonFarmGameData.Instance.GetDoodadGuardTime(doodad.Template.GroupId);
            if (DateTime.UtcNow < doodad.PlantTime.AddSeconds(guardTime)) { continue; }

            // defense time is up
            doodad.OwnerId = 0;
            doodad.OwnerObjId = 0;
            doodad.OwnerType = DoodadOwnerType.System;
            doodad.FarmType = FarmType.Invalid;
            DoodadManager.Instance.RefreshFaction(doodad);
            doodad.Save();
            deleted.Add(doodad);
        }

        foreach (var doodad in deleted)
        {
            // doodad.Delete
            world.SpawnManager?.RemovePlayerDoodad(doodad);
        }
    }

    public bool InPublicFarm(WorldTemplate worldTemplate, Vector3 pos)
    {
        var subZoneList = subZoneManager.GetSubZoneByPosition(worldTemplate, pos);
        return subZoneList.Count > 0 && subZoneList.Any(subZoneId => _farmZones.ContainsKey(subZoneId));
    }

    private uint GetFarmId(WorldInstance world, Vector3 pos)
    {
        var subZoneList = subZoneManager.GetSubZoneByPosition(world.Template, pos);

        return subZoneList.Count > 0 ? subZoneList.FirstOrDefault(subZoneId => _farmZones.ContainsKey(subZoneId)) : 0;
    }

    public FarmType GetFarmType(WorldInstance world, Vector3 pos)
    {
        var subZoneId = GetFarmId(world, pos);
        return _farmZones.GetValueOrDefault(subZoneId, FarmType.Invalid);
    }

    public bool CanPlace(Character character, FarmType farmType, uint doodadId)
    {
        var allPlanted = GetCommonFarmDoodads(character);
        if (allPlanted.TryGetValue(farmType, out var doodadList))
        {
            if (doodadList.Count >= CommonFarmGameData.Instance.GetFarmGroupMaxCount(farmType))
            {
                character.SendErrorMessage(Models.Game.ErrorMessageType.CommonFarmCountOver);
                return false;
            }
        }

        var allowedDoodads = CommonFarmGameData.Instance.GetAllowedDoodads(farmType);
        if (allowedDoodads.Any(id => doodadId == id))
        {
            return true;
        }

        character.SendErrorMessage(Models.Game.ErrorMessageType.CommonFarmNotAllowedType);
        return false;
    }

    public Dictionary<FarmType, List<Doodad>> GetCommonFarmDoodads(Character character)
    {
        var list = new Dictionary<FarmType, List<Doodad>>();

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

    public static bool IsProtected(Doodad doodad)
    {
        var guardTime = CommonFarmGameData.Instance.GetDoodadGuardTime(doodad.Template.GroupId);
        var protectionTime = doodad.PlantTime.AddSeconds(guardTime);

        return DateTime.UtcNow < protectionTime;
    }

    public void Load()
    {
        // Common-farm subzone ids, hand-maintained.
        //
        // KNOWN GAP: this map is lossy. common_farms ships 46 rows, and no farm table in the catalog
        // carries a subzone or zone column at all (common_farms is id, name, guard_time,
        // farm_group_id, comments; farm_groups is id, name, count with only 2 rows, so it does not
        // discriminate farm type either). Only 5 subzone ids are covered here, leaving 41 of the 46
        // authored farms unreachable: a position in those subzones resolves to FarmType.Invalid and
        // is skipped. The only field that could correlate them is the localized farm name, which is
        // display text and must not be used for classification. A real mapping needs content that
        // does not exist yet; the ids below are therefore a partial, hand-kept approximation.
        _farmZones = new Dictionary<uint, FarmType>
        {
            { 998, FarmType.Farm },
            { 966, FarmType.Farm },
            { 968, FarmType.Nursery },
            { 967, FarmType.Ranch },
            { 974, FarmType.Stable }
        };
    }

}
