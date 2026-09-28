using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.CommonFarm;
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
        if (world?.SpawnManager == null) { return; }

        // Two phases, and the split is not optional. Deleting a doodad unlinks it from the
        // spawn manager's live player-doodad list, which is the very list being walked here, so
        // collecting first and deleting afterwards is what keeps the pass from throwing part way
        // through and leaving the farm half-expired.
        var now = DateTime.UtcNow;
        var expired = new List<Doodad>();
        var unconfiguredGroups = new HashSet<uint>();

        foreach (var doodad in world.SpawnManager.GetAllPlayerDoodads())
        {
            if (doodad.FarmType == FarmType.Invalid) { continue; }

            var groupId = doodad.Template?.GroupId ?? 0;
            var decision = CommonFarmExpiryRules.Evaluate(
                CommonFarmGameData.Instance.TryGetDoodadGuardTime(groupId, out var guardSeconds),
                guardSeconds,
                doodad.PlantTime,
                now);

            switch (decision)
            {
                case CommonFarmExpiryDecision.Expire:
                    expired.Add(doodad);
                    break;

                case CommonFarmExpiryDecision.GuardTimeNotConfigured:
                    // The crop is left exactly where it is. Content names no protection window for
                    // its doodad group, so its age cannot be compared against anything, and retiring
                    // it on a length nobody wrote would take the crop the moment it was planted.
                    unconfiguredGroups.Add(groupId);
                    break;

                default:
                    break;
            }
        }

        foreach (var groupId in unconfiguredGroups)
        {
            Logger.Error("CommonFarm: no doodad_groups row protects crops of doodad group {0}, so their "
                         + "protection window has no length. They are kept in the field rather than "
                         + "retired on a value content never supplied.", groupId);
        }

        foreach (var doodad in expired)
        {
            // Delete, not re-save. Re-saving only rewrote the row into a system-owned doodad and
            // dropped it from the farm list while the doodad itself stayed in the world and on the
            // player's screen; it also left a row that nothing ever read again. Deleting removes the
            // world object, the client object and the row together, so a World restart cannot bring
            // an expired crop back.
            Logger.Debug("CommonFarm: protection window of {0}s is up for doodad {1} (template {2}); "
                         + "removing it from the farm.", (now - doodad.PlantTime).TotalSeconds, doodad.ObjId,
                doodad.TemplateId);
            doodad.Delete();
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

    /// <summary>
    /// The farm tab a position sits on, together with the positions already planted in it.
    /// </summary>
    /// <param name="world">The world the position is in.</param>
    /// <param name="pos">The position to resolve.</param>
    /// <param name="farmType">The tab the land carries, or <see cref="FarmType.Invalid"/> off the farm.</param>
    /// <param name="positions">Every planted crop in that tab, whoever planted it.</param>
    /// <remarks>
    /// The list is the whole tab's contents, not the caller's own crops: an area view is about the
    /// land, and a response listing only what one player planted would be a different thing. An
    /// empty result is a real answer and not an error â€” the response writes a zero count for it,
    /// which is what makes the reader drop the positions it was holding.
    /// </remarks>
    public void GetFarmArea(WorldInstance world, Vector3 pos, out FarmType farmType, out List<Vector3> positions)
    {
        farmType = GetFarmType(world, pos);
        positions = [];

        if (world?.SpawnManager == null) { return; }

        foreach (var doodad in world.SpawnManager.GetAllPlayerDoodads())
        {
            if (doodad.FarmType != farmType) { continue; }
            positions.Add(doodad.Transform.World.Position);
        }
    }

    public bool CanPlace(Character character, FarmType farmType, uint doodadId)
    {
        // A farm tab the player has no crops in yet has no dictionary entry, so the count is read
        // as zero rather than skipping the capacity check altogether.
        var plantedCount = GetCommonFarmDoodads(character).TryGetValue(farmType, out var planted)
            ? planted.Count
            : 0;

        var refusal = CommonFarmPlacementRules.Evaluate(
            doodadAllowed: CommonFarmGameData.Instance.GetAllowedDoodads(farmType).Contains(doodadId),
            capacityConfigured: CommonFarmGameData.Instance.TryGetFarmGroupMaxCount(farmType, out var capacity),
            capacity: capacity,
            plantedCount: plantedCount);

        switch (refusal)
        {
            case CommonFarmPlacementRefusal.None:
                return true;

            case CommonFarmPlacementRefusal.CapacityNotConfigured:
                // Content gives this farm tab no size. Nothing is sent to the player: the only
                // client message for it is the same one a full farm uses, and telling someone their
                // farm is full when the farm's size was never written down is a false answer. The
                // refusal is logged instead, naming the tab, so the gap is loud in the server log and
                // silent where it cannot be told apart from the truth.
                Logger.Error("CommonFarm: farm type {0} has no farm_groups row, so no crop capacity is "
                             + "defined for it. Refusing the placement of doodad {1} for {2} rather than "
                             + "assuming one.", farmType, doodadId, character?.Name ?? "<no character>");
                return false;

            case CommonFarmPlacementRefusal.CapacityReached:
                character.SendErrorMessage(Models.Game.ErrorMessageType.CommonFarmCountOver);
                return false;

            default:
                character.SendErrorMessage(Models.Game.ErrorMessageType.CommonFarmNotAllowedType);
                return false;
        }
    }

    /// <summary>
    /// Takes one planted crop back out of the field.
    /// </summary>
    /// <param name="doodad">The crop to remove.</param>
    /// <param name="caller">Who asked for it, for the log line only.</param>
    /// <returns>
    /// <c>false</c> when the crop is not a planted crop, is owned by somebody else, or is still
    /// inside its protection window. In every one of those cases nothing is changed.
    /// </returns>
    /// <remarks>
    /// This removes exactly one crop and always says why it did not, so it cannot become the bulk
    /// clear that a bare "remove the player's farms" request once was. The crop is deleted rather
    /// than released: a released crop keeps its row, and a released crop is a crop a World restart
    /// would load again.
    /// </remarks>
    public bool RemoveCrop(Doodad doodad, Character caller)
    {
        if (doodad == null || doodad.FarmType == FarmType.Invalid)
        {
            Logger.Warn("CommonFarm: asked to remove doodad {0}, which is not a planted crop. "
                        + "Nothing changed.", doodad?.ObjId ?? 0);
            return false;
        }

        if (caller != null && doodad.OwnerType == DoodadOwnerType.Character && doodad.OwnerId != caller.Id)
        {
            Logger.Warn("CommonFarm: {0} asked to remove crop {1} owned by character {2}. Refusing: a "
                        + "crop is only ever removed by the character that planted it.",
                caller.Name, doodad.ObjId, doodad.OwnerId);
            return false;
        }

        if (IsProtected(doodad))
        {
            Logger.Warn("CommonFarm: {0} asked to remove crop {1}, which is still inside its protection "
                        + "window. Refusing.", caller?.Name ?? "<server>", doodad.ObjId);
            return false;
        }

        doodad.Delete();
        return true;
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

    /// <summary>
    /// Whether a crop is still inside the window content gives it.
    /// </summary>
    /// <returns>
    /// <c>true</c> while the window is open â€” and also while its length is unknown, because a crop
    /// whose age cannot be compared to anything is not known to be harvestable.
    /// </returns>
    public static bool IsProtected(Doodad doodad)
    {
        if (!CommonFarmGameData.Instance.TryGetDoodadGuardTime(doodad.Template?.GroupId ?? 0, out var guardSeconds))
        {
            return true;
        }

        var protectionTime = doodad.PlantTime.AddSeconds(guardSeconds);

        return DateTime.UtcNow < protectionTime;
    }

    public void Load()
    {
        //common farm subzone ID's
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
