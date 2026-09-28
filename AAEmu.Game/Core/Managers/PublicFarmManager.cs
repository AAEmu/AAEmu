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

using NLog;

namespace AAEmu.Game.Core.Managers;

public class PublicFarmManager(ISubZoneManager subZoneManager) : Singleton<PublicFarmManager>, IPublicFarmManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, FarmType> _farmZones;

    /// <summary>
    /// Farm tabs already reported as having no authored capacity, so the content gap is logged once
    /// per tab rather than on every placement attempt.
    /// </summary>
    private readonly HashSet<FarmType> _reportedCapacityGaps = [];

    public void Initialize()
    {
        Logger.Info("Initialising Public Farm Manager...");
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

        var capacityConfigured = CommonFarmGameData.Instance.TryGetFarmGroupMaxCount(farmType, out var capacity);
        var refusal = CommonFarmPlacementRules.Evaluate(
            doodadAllowed: CommonFarmGameData.Instance.GetAllowedDoodads(farmType).Contains(doodadId),
            capacityConfigured: capacityConfigured,
            capacity: capacity,
            plantedCount: plantedCount);

        // Content gives this farm tab no size, so no capacity is enforced for it. Nothing is sent to
        // the player: the only client message for a full farm would be a false one, and refusing
        // would make a tab that content clearly lists crops for unplantable. The gap is logged
        // instead, once per tab, so it is loud in the server log and absent where it would be a lie.
        if (!capacityConfigured && _reportedCapacityGaps.Add(farmType))
        {
            Logger.Error("CommonFarm: farm type {0} has no farm_groups row, so no crop capacity is "
                         + "defined for it. Allowing placements with no limit rather than inventing "
                         + "one; content authors the tab by listing its crops, so the tab is meant to "
                         + "be plantable.", farmType);
        }

        switch (refusal)
        {
            case CommonFarmPlacementRefusal.None:
                return true;

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
    /// Whether a crop is still inside the protection window content gives it.
    /// </summary>
    /// <returns>
    /// <c>true</c> while the window is open â€” and also while its length is unknown, because a crop
    /// whose age cannot be compared to anything is not known to be harvestable.
    /// </returns>
    /// <remarks>
    /// The window decides <b>who may take a crop</b>, not how long a crop lives. A crop past its
    /// window is still in the field and is simply unprotected, which is how the client's farm list
    /// renders it. Nothing retires a crop for being old: most doodad groups ship a window of zero,
    /// and a pass that read that as a lifetime would clear the field a minute after planting.
    /// </remarks>
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
