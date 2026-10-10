using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Indun.Events;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using NLog;

namespace AAEmu.Game.Core.Managers;

// ReSharper disable once ClassNeverInstantiated.Global
public class IndunManager(ITickManager tickManager, IWorldManager worldManager, IZoneManager zoneManager, ITeamManager teamManager) : Singleton<IndunManager>, IIndunManager
{
    // ReSharper disable once InconsistentNaming
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, Dictionary<uint, List<DateTime>>> EntryHistory { get; } = []; // <ownerId, <zoneGroupId, entry time>> - dungeon attempts used
    /// <summary>Last time this character created a new copy of a zone group (restore_item_time gate).</summary>
    private Dictionary<uint, Dictionary<uint, DateTime>> CreateHistory { get; } = [];
    /// <summary>How many IVT_RESET tickets this character has bought today per zone group.</summary>
    private Dictionary<uint, Dictionary<uint, int>> ResetPurchaseCount { get; } = [];
    /// <summary>Extra daily enters from IVT_PERMIT tickets (lifetime of process / daily window).</summary>
    private Dictionary<uint, Dictionary<uint, int>> PermitBonusCount { get; } = [];
    // ReSharper disable once ChangeFieldTypeToSystemThreadingLock
    private readonly object _lock = new();
    internal Func<DateTime> AdmissionUtcNow { get; set; } = () => ServerCalendar.UtcNow;
    internal Func<Character, InstancePermissionTagKind, uint, bool> AdmissionTagMatcher { get; set; } =
        CharacterHasPermissionTag;

    /// <summary>
    /// Zone-permission asks this server opened and that no answer has settled yet, by character id.
    /// This is the authority behind CS 0x058: an answer only changes permission for a character
    /// holding an open ask, and the ask is consumed by the answer.
    /// </summary>
    private readonly Dictionary<uint, ZonePermissionSession> _zonePermissionAsks = [];

    /// <summary>
    /// Opens the zone-permission ask for a character. Nothing in the live path calls this yet:
    /// the answer packet stays a stub until an ask is opened and the permission-state refresh
    /// has a body. This records the authority that answer will be judged against.
    /// </summary>
    /// <param name="character">Who is being asked.</param>
    /// <param name="zoneGroupId">The zone group the ask is for; 0 is not a zone group and is refused.</param>
    /// <returns>True when the ask was opened.</returns>
    public bool OpenZonePermissionAsk(Character character, uint zoneGroupId)
    {
        if (character == null || zoneGroupId == 0)
        {
            Logger.Warn("OpenZonePermissionAsk refused: characterId={0} zoneGroupId={1}",
                character?.Id ?? 0u, zoneGroupId);
            return false;
        }

        lock (_lock)
            _zonePermissionAsks[character.Id] = new ZonePermissionSession(zoneGroupId, ServerCalendar.UtcNow);

        Logger.Info("Zone permission ask opened char={0} zoneGroup={1}", character.Name, zoneGroupId);
        return true;
    }

    /// <summary>True while this character still owes the server an answer.</summary>
    public bool HasOpenZonePermissionAsk(uint characterId)
    {
        lock (_lock)
            return _zonePermissionAsks.ContainsKey(characterId);
    }

    /// <summary>
    /// Judges a CS 0x058 answer against the open ask. Only an open ask may settle permission: a
    /// solicited answer consumes it (Accepted on OK, Declined on Cancel), anything without one changes
    /// nothing and is logged, and a body byte the dialog cannot produce leaves the ask untouched.
    /// </summary>
    public ZonePermissionVerdict AnswerZonePermission(Character character, byte answer)
    {
        if (character == null)
            return ZonePermissionVerdict.NoOpenAsk;

        lock (_lock)
        {
            if (!_zonePermissionAsks.TryGetValue(character.Id, out var ask))
            {
                Logger.Warn("Zone permission answer with no open ask: char={0} answer={1}",
                    character.Id, answer);
                return ZonePermissionVerdict.NoOpenAsk;
            }

            if (answer > 1)
            {
                Logger.Warn(
                    "Malformed zone permission answer {0}: char={1} zoneGroup={2} — ask left open",
                    answer, character.Id, ask.ZoneGroupId);
                return ZonePermissionVerdict.Malformed;
            }

            _zonePermissionAsks.Remove(character.Id);
            Logger.Info("Zone permission {0} char={1} zoneGroup={2}",
                answer == 1 ? "accepted" : "declined", character.Id, ask.ZoneGroupId);
            return answer == 1 ? ZonePermissionVerdict.Accepted : ZonePermissionVerdict.Declined;
        }
    }

    public void Initialize()
    {
        tickManager.OnTick.Subscribe(IndunInfoTick, TimeSpan.FromSeconds(30), true);
    }

    private void IndunInfoTick(TimeSpan delta)
    {
        var sysInstanceCount = 0;
        var dungeonInstanceCount = 0;
        var worldList = worldManager.GetWorlds().ToList();

        // Count dungeons
        foreach (var worldInstance in worldList)
        {
            if (worldInstance.DungeonInstance != null)
            {
                if (worldInstance.DungeonInstance.IsSystem)
                {
                    sysInstanceCount++;
                }
                else
                {
                    dungeonInstanceCount++;
                }
            }
        }

        // Refresh every copy's HUD readouts (0x2D8). The client ticks the time itself between updates, so the
        // copy's existing coarse cadence is the right place for them.
        foreach (var worldInstance in worldList)
            worldInstance.DungeonInstance?.SendPlayingInfoBroadcast();

        if (sysInstanceCount + dungeonInstanceCount <= 0)
            return;
        
        Logger.Info($"Active Instances: {sysInstanceCount} system instance(s), {dungeonInstanceCount} dungeon(s)");

        if (dungeonInstanceCount <= 0)
            return;

        // enumerate dungeon info
        foreach (var worldInstance in worldList)
        {
            if (worldInstance.DungeonInstance != null)
            {
                Logger.Debug($"{worldInstance} - used by {worldInstance.GetCharacterCount()}/{worldInstance.DungeonInstance.PlayersWithAccess.Count} player(s): {worldInstance.ListPlayerNames(10)}");
                if (worldInstance.DungeonInstance.IsExpired)
                {
                    Logger.Warn($"Removing expired solo dungeon {worldInstance}");
                    worldInstance.DungeonInstance.DestroyDungeon();
                }
            }
        }

        InfoAttempt();
    }

    /// <summary>
    /// Checks if the dungeon for a given zone requires a channel select
    /// </summary>
    /// <param name="zoneId"></param>
    /// <returns></returns>
    public bool InstanceHasChannels(uint zoneId)
    {
        var dungeonZone = IndunGameData.Instance.GetDungeonZone(zoneManager.GetZoneById(zoneId).GroupId);
        return dungeonZone.SelectChannel;
    }

    /// <summary>
    /// The dimension each character last picked in the channel list, so the entry that follows knows which copy
    /// to put them in. The client's enter request carries no channel of its own.
    /// </summary>
    private readonly Dictionary<uint, SysIndunPick> _instancePicks = [];

    /// <summary>
    /// Sends the channel list for a system instance. The client's picker opens on this packet — it never asks
    /// for the list — so it goes out when the player reaches one of the instance's entrances.
    /// </summary>
    /// <param name="character">Who is at the entrance.</param>
    /// <param name="zoneId">The instance zone they are entering (a <c>zones.id</c>).</param>
    /// <returns>True when a list was sent.</returns>
    public bool SendChannelList(Character character, uint zoneId)
    {
        if (character == null)
            return false;

        var zone = zoneManager.GetZoneById(zoneId);
        if (zone == null)
        {
            Logger.Warn("SendChannelList: no zone {0} for {1}", zoneId, character.Name);
            return false;
        }

        var dungeonZone = IndunGameData.Instance.GetDungeonZone(zone.GroupId);
        if (dungeonZone == null)
        {
            Logger.Warn("SendChannelList: zone {0} (group {1}) is not an instance", zoneId, zone.GroupId);
            return false;
        }

        var zoneKeys = zoneManager.GetZoneKeysInZoneGroupById(dungeonZone.ZoneGroupId);
        if (zoneKeys == null || zoneKeys.Count == 0)
        {
            Logger.Warn("SendChannelList: zone group {0} has no zone keys", dungeonZone.ZoneGroupId);
            return false;
        }

        var rows = SysIndunChannelRules.BuildOfferable(GetChannelsOfZoneGroup(dungeonZone), (int)dungeonZone.MaxPlayers);
        if (rows.Count == 0)
        {
            // Nothing serves a copy of this instance, so there is no dimension to offer. That is a hosting
            // problem (the copies are declared and run like any other zone), not something to paper over by
            // creating copies nothing will load.
            Logger.Warn("SendChannelList: no hosted copy of zone group {0} (instance {1}) for {2} - " +
                        "nothing to offer; are its channel copies declared and running?",
                dungeonZone.ZoneGroupId, dungeonZone.InstanceCatalogId, character.Name);
            return false;
        }

        character.SendPacket(new SCSysIndunStatPacket(dungeonZone.ZoneGroupId, rows));

        Logger.Info("SendChannelList char={0} zoneGroup={1} instanceId={2} channels={3} capacity={4}",
            character.Name, dungeonZone.ZoneGroupId, dungeonZone.InstanceCatalogId, rows.Count,
            dungeonZone.MaxPlayers);
        return true;
    }

    /// <summary>
    /// The instance portals in the world the character is standing in, sent as the world-entry burst opens.
    /// </summary>
    /// <remarks>
    /// The client fills its instance window from this packet and never asks for the list, so a world that does
    /// not send it leaves the window showing only what the client can work out on its own — which is the
    /// difference between a window with the instances in it and one with a handful of entries and dead tabs.
    /// The rows come from the enter-instance doodads of that world: the func names the instance the portal
    /// leads to, the doodad supplies the zone it stands in and the position the window points at.
    /// </remarks>
    /// <param name="character">Who entered the world.</param>
    /// <returns>The number of portals sent.</returns>
    public int SendPortalList(Character character)
    {
        if (character == null)
            return 0;

        var world = character.ParentWorld;
        if (world == null)
        {
            Logger.Warn("SendPortalList: {0} has no world to read instance portals from", character.Name);
            return 0;
        }

        var doodads = UnitManagers.DoodadManager.Instance;
        var rows = new List<IndunPortalPoint>();
        var droppedByRequirement = 0;

        foreach (var doodad in world.GetAllDoodads())
        {
            var template = doodad?.Template;
            if (template == null)
                continue;

            var position = doodad.Transform.World.Position;
            foreach (var group in template.FuncGroups)
            {
                foreach (var func in doodads.GetFuncsForGroup(group.Id))
                {
                    if (!IndunPortalListRules.IsInstancePortalFunc(func.FuncType))
                        continue;

                    var instanceZone = doodads.GetFuncTemplate(func.FuncId, func.FuncType) switch
                    {
                        Models.Game.DoodadObj.Funcs.DoodadFuncEnterInstance enter => enter.ZoneId,
                        Models.Game.DoodadObj.Funcs.DoodadFuncEnterSysInstance enterSys => enterSys.ZoneId,
                        _ => 0u
                    };

                    if (instanceZone == 0)
                        continue;

                    // Only portals the client's window has an entry for and the character may enter: the
                    // target has to be a zone we know, it has to be an instance, and the instance's own
                    // level/gear requirements have to pass. Everything else is a door that goes nowhere.
                    var targetZone = zoneManager.GetZoneById(instanceZone);
                    if (targetZone == null)
                        continue;

                    var dungeon = IndunGameData.Instance.GetDungeonZone(targetZone.GroupId);
                    if (dungeon == null)
                        continue;

                    if (!IndunPortalListRules.CanEnter(
                            dungeon.LevelMin, dungeon.LevelMax, dungeon.GearScore,
                            character.Level, character.GearScore))
                    {
                        droppedByRequirement++;
                        Logger.Debug(
                            "SendPortalList: zoneGroup={0} dropped for char={1} (level {2} of {3}~{4}, gear {5} of {6})",
                            targetZone.GroupId, character.Name, character.Level,
                            dungeon.LevelMin, dungeon.LevelMax, character.GearScore, dungeon.GearScore);
                        continue;
                    }

                    // The window keys every entry by zone GROUP, not by zone: its Lua pulls the list from
                    // X2Indun:GetIndunList() and looks each row up with FillContent(zoneGroup). A zone id
                    // there resolves to nothing, so the row is dropped and the window looks untouched.
                    var portalZone = zoneManager.GetZoneById(doodad.Transform.ZoneId);
                    if (portalZone == null)
                        continue;

                    rows.Add(new IndunPortalPoint(
                        targetZone.GroupId, portalZone.GroupId, position.X, position.Y, position.Z));
                }
            }
        }

        var portals = IndunPortalListRules.Build(rows);
        character.SendPacket(new SCIndunPortalsPacket(portals));

        if (portals.Count == 0)
        {
            // Loud on purpose: a world that finds no instance portal doodads advertises an empty window on
            // the client, which is indistinguishable from the client ignoring the packet. Warn reaches the
            // file log; Info does not.
            Logger.Warn("SendPortalList char={0} world={1} found no instance portal doodads",
                character.Name, world.Id);
        }
        else
        {
            Logger.Info("SendPortalList char={0} world={1} portals={2}",
                character.Name, world.Id, portals.Count);
        }

        // The character's own numbers ride along: the window's Enter gate is a level / equipment-points
        // check, so a refusal has to be readable without guessing which of the two failed.
        Logger.Info(
            "SendPortalList char={0} world={1} level={2} gearScore={3} portals={4} droppedByRequirement={5}",
            character.Name, world.Id, character.Level, character.GearScore, portals.Count, droppedByRequirement);

        return portals.Count;
    }

    /// <summary>The copies of an instance that exist now, each with whether a host is serving it.</summary>
    private IEnumerable<SysIndunChannelCopy> GetChannelsOfZoneGroup(IndunZone dungeonZone)
    {
        foreach (var zoneKey in zoneManager.GetZoneKeysInZoneGroupById(dungeonZone.ZoneGroupId))
        {
            foreach (var dungeon in GetExistingDungeonsByZoneKey(zoneKey))
            {
                var world = dungeon.World;
                if (world == null)
                    continue;

                var hosted = SysIndunChannelRules.CopyIsHosted(
                    WorldIntegration.IsZoneInstanceLoaded, zoneKey, world.Id);
                yield return new SysIndunChannelCopy(
                    new SysIndunChannel((int)world.ChannelId, world.Id, world.GetCharacterCount(),
                        (int)dungeonZone.MaxPlayers),
                    hosted);
            }
        }
    }

    /// <summary>Remembers which dimension a character picked, for the entry that follows.</summary>
    public void RememberInstancePick(uint characterId, SysIndunPick pick)
    {
        lock (_lock)
            _instancePicks[characterId] = pick;
    }

    /// <summary>The dimension a character last picked, or null when they never opened the list.</summary>
    public SysIndunPick? GetInstancePick(uint characterId)
    {
        lock (_lock)
            return _instancePicks.TryGetValue(characterId, out var pick) ? pick : null;
    }

    /// <summary>
    /// Drops the remembered dimension once an entry has used it, so it cannot decide a later one.
    /// </summary>
    public void ClearInstancePick(uint characterId)
    {
        lock (_lock)
            _instancePicks.Remove(characterId);
    }

    /// <summary>
    /// The instance a world copy belongs to, by that copy's id — how a channel the client picked is traced
    /// back to the zone it is a copy of.
    /// </summary>
    public IndunZone GetDungeonZoneOfCopy(uint worldId)
    {
        foreach (var worldInstance in worldManager.GetWorlds())
        {
            if (worldInstance.Id != worldId)
                continue;

            return worldInstance.DungeonInstance?._indunZone;
        }

        return null;
    }

    /// <summary>
    /// Requests an instance for the character's team or for the player.
    /// </summary>
    /// <param name="character"></param>
    /// <param name="zoneId"></param>
    /// <param name="channelId"></param>
    /// <param name="dungeon"></param>
    /// <returns></returns>
    public bool RequestSystemInstance(Character character, uint zoneId, uint channelId, out Dungeon dungeon)
    {
        dungeon = null;
        if (character == null)
        {
            Logger.Info("[IndunManager] Player offline.");
            return false;
        }

        var zone = zoneManager.GetZoneById(zoneId);
        if (zone == null)
        {
            Logger.Warn($"Requesting non existing system instance for zone {zoneId}, character {character.Name}");
            return false;
        }

        var dungeonZone = IndunGameData.Instance.GetDungeonZone(zone.GroupId);
        if (dungeonZone == null || !VerifyDungeonAdmission(dungeonZone, character))
            return false;

        foreach (var possibleDungeon in GetExistingDungeonsByZoneKey(zone.ZoneKey))
        {
            if (possibleDungeon.World.ChannelId == channelId)
            {
                dungeon = possibleDungeon;
                
                return dungeon.QueuePlayer(character);
            }
        }

        dungeon = CreateSystemInstance(character, zone.ZoneKey, channelId);
        if (dungeon == null)
        {
            Logger.Error($"Failed to create system instance for zoneId {zoneId}, channel: {channelId}, character {character.Name}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Requests an instance for the character's team or for the player.
    /// </summary>
    /// <param name="character"></param>
    /// <param name="zoneId"></param>
    /// <param name="channelId"></param>
    /// <param name="pickedCopyId">
    /// The copy the character picked in the channel list, when the entry follows a pick. It decides the copy:
    /// the access rules below match a party's own dungeon, which a shared dimension is not.
    /// </param>
    /// <returns></returns>
    public bool RequestDungeonInstance(Character character, uint zoneId, uint channelId, uint? pickedCopyId = null)
    {
        if (character == null)
        {
            Logger.Info($"Player requested a dungeon, but is now offline.");
            return false;
        }
        var team = teamManager.GetTeamByObjId(character.ObjId);
        var zone = zoneManager.GetZoneById(zoneId);

        // Check valid zone/dungeon
        var worldTemplate = worldManager.GetWorldTemplateByZoneKey(zone.ZoneKey);
        if (worldTemplate == null)
        {
            // Non-existing dungeon zone
            return false;
        }

        var targetZone = zoneManager.GetZoneById(zoneId);
        if (targetZone == null)
        {
            // Key does not match any zone
            return false;
        }
        
        var dungeonZone = IndunGameData.Instance.GetDungeonZone(targetZone.GroupId);
        if (dungeonZone == null)
        {
            // Not a dungeon
            return false;
        }

        // No schedule or tag check here: the rejoin paths below run first and must stay reachable while the
        // entrance window is closed, because instances 50/51/55 are reentry=true and a member who drops
        // mid-run has to get back into the copy their party already paid for. Every fresh entry is still
        // gated by VerifyDungeonEnterRequirements.
        var possibleTargetInstances = GetExistingDungeonsByZoneKey(targetZone.ZoneKey);

        // A pick names the dimension to land in, so it is settled before the access rules below: those look for
        // a party's own copy, which a shared dimension is not. Only channel instances honour one — an ordinary
        // dungeon keeps its rejoin and team rules, including the visit-limit skip those paths use.
        if (pickedCopyId is { } wantedCopy &&
            SysIndunChannelRules.HonourPick(dungeonZone.SelectChannel, wantedCopy))
        {
            // Only a copy a host is still serving counts: one that dropped between the list and the entry would
            // put the player in a copy nothing simulates, so it is refused rather than silently substituted.
            var hostedCopyId = SysIndunChannelRules.FindCopyForPick(
                GetChannelsOfZoneGroup(dungeonZone), wantedCopy, null);
            var picked = hostedCopyId is { } copyId
                ? possibleTargetInstances.FirstOrDefault(candidate => candidate.World?.Id == copyId)
                : null;

            if (picked == null)
            {
                Logger.Warn("RequestDungeonInstance: picked copy {0} of zone {1} is not hosted for {2}",
                    wantedCopy, targetZone.ZoneKey, character.Name);
                character.SendErrorMessage(ErrorMessageType.NoServerInstanceResource);
                return false;
            }

            // A pick names the copy, not a way around the instance's own requirements.
            if (!VerifyDungeonEnterRequirements(dungeonZone, character, team))
                return false;

            if (IsDungeonFull(picked.World.GetCharacterCount(), picked._indunZone.MaxPlayers))
            {
                character.SendErrorMessage(ErrorMessageType.InstanceQuota);
                return false;
            }

            Logger.Info("RequestDungeonInstance: entering picked copy {0} (channel {1}) for {2}",
                picked.World.Id, picked.World.ChannelId, character.Name);
            return picked.QueuePlayer(character);
        }

        // Rejoin a copy this player already paid for — do not apply the daily cap again.
        foreach (var possibleTargetInstance in possibleTargetInstances)
        {
            if (possibleTargetInstance.EnterRequests.Contains(character))
                return possibleTargetInstance.QueuePlayer(character);
            if (possibleTargetInstance.World.HasCharacter(character.Id))
            {
                possibleTargetInstance.AddPlayer(character);
                return true;
            }
            if (possibleTargetInstance.HasChargedEntry(character.Id))
            {
                if (IsDungeonFull(possibleTargetInstance.World.GetCharacterCount(), possibleTargetInstance._indunZone.MaxPlayers))
                {
                    character.SendErrorMessage(ErrorMessageType.InstanceQuota);
                    return false;
                }

                return possibleTargetInstance.QueuePlayer(character);
            }
        }

        // Check level (or other stat) requirements
        if (!VerifyDungeonEnterRequirements(dungeonZone, character, team))
        {
            return false;
        }

        // First visit: party access list (not yet charged) or a new copy.
        foreach (var possibleTargetInstance in possibleTargetInstances)
        {
            if (possibleTargetInstance.PlayersWithAccess.Contains(character.Id))
            {
                if (IsDungeonFull(possibleTargetInstance.World.GetCharacterCount(), possibleTargetInstance._indunZone.MaxPlayers))
                {
                    character.SendErrorMessage(ErrorMessageType.InstanceQuota);
                    return false;
                }

                return possibleTargetInstance.QueuePlayer(character);
            }
        }

        // 2 - First check Party required dungeons is available
        if (dungeonZone.PartyOnly) // Only if dungeon requires party
        {
            foreach (var possibleTargetInstance in possibleTargetInstances)
            {
                if (!possibleTargetInstance.PlayerInSameTeam(character))
                    continue;
                
                // Join your team's dungeon (if enough room)
                if (possibleTargetInstance.IsFull)
                {
                    character.SendErrorMessage(ErrorMessageType.InstanceQuota); // Too many users are currently in the dungeon
                    return false;
                }
                
                return possibleTargetInstance.QueuePlayer(character);
            }
        }

        // 3 - Check if non-party/raid leader is a member of the requested dungeon, if so, join their instance
        if (team != null)
        {
            // 3a - Create a list of players to check with party leader as first entry
            // The rest is the same order as the team order
            var checkPlayersList = new List<Character>();
            foreach (var teamMember in team.Members)
            {
                if (teamMember == null || teamMember.Character == null)
                    continue;
                if (teamMember.Character.Id == team.OwnerId)
                {
                    checkPlayersList.Insert(0, teamMember.Character);
                }
                else
                {
                    checkPlayersList.Add(teamMember.Character);
                }
            }

            // 3b - Enumerate the sorted team member list to check if we have a matching dungeon to enter
            foreach (var playerCharacter in checkPlayersList)
            {
                foreach (var possibleTargetInstance in possibleTargetInstances)
                {
                    if (!possibleTargetInstance.PlayersWithAccess.Contains(playerCharacter.Id))
                        continue;
                
                    // Join your team's dungeon (if enough room)
                    // TODO: not sure if we should toss a error here, or continue searching for others
                    if (possibleTargetInstance.IsFull)
                    {
                        character.SendErrorMessage(ErrorMessageType.InstanceQuota); // Too many users are currently in the dungeon
                        return false;
                    }

                    return possibleTargetInstance.QueuePlayer(character);
                }
            }
        }

        // 4 - If none of the above applies, actually create a new dungeon
        Logger.Info($"Creating a new dungeon for player {character.Name} ({character.Id}), zone: {dungeonZone}, channel: {channelId}");
        if (!CreateDungeonInstance(dungeonZone, character, channelId, out _))
        {
            Logger.Error($"Failed to create a new dungeon for player {character.Name} ({character.Id}), zone: {dungeonZone}, channel: {channelId}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Creates a list of all currently active dungeons that have a given zone
    /// </summary>
    /// <param name="zoneKey">Required Zone Key for the dungeons</param>
    /// <returns></returns>
    private List<Dungeon> GetExistingDungeonsByZoneKey(uint zoneKey)
    {
        var res = new List<Dungeon>();
        foreach (var worldInstance in worldManager.GetWorlds())
        {
            if (worldInstance.DungeonInstance == null)
                continue;
            if (worldInstance.Template.ZoneKeys.Contains(zoneKey))
                res.Add(worldInstance.DungeonInstance);
        }
        return res;
    }

    /// <summary>
    /// Check if the player has the level, items and other requirements to be allowed to enter the given dungeon zone
    /// </summary>
    /// <param name="dungeonZone"></param>
    /// <param name="character"></param>
    /// <param name="team"></param>
    /// <returns></returns>
    private bool VerifyDungeonEnterRequirements(IndunZone dungeonZone, Character character, Team team)
    {
        if (!VerifyDungeonAdmission(dungeonZone, character))
            return false;

        // Check access count
        if (!CheckEntryAttemptCount(character.Id, dungeonZone.ZoneGroupId, dungeonZone, false))
        {
            
            character.SendErrorMessage(ErrorMessageType.InstanceVisitLimit);
            return false;
        }

        // Check Level requirement
        if (character.Level < dungeonZone.LevelMin)
        {
            Logger.Warn($"Requesting instance level too low ({character.Level} < {dungeonZone.LevelMin}), characterId: {character.Id}, zoneGroupId: {dungeonZone.ZoneGroupId}");
            character.SendErrorMessage(ErrorMessageType.InstanceLevel);
            return false;
        }
        if (character.Level > dungeonZone.LevelMax)
        {
            Logger.Warn($"Requesting instance level too high ({character.Level} > {dungeonZone.LevelMax}), characterId: {character.Id}, zoneGroupId: {dungeonZone.ZoneGroupId}");
            character.SendErrorMessage(ErrorMessageType.InstanceLevel);
            return false;
        }

        // Check gear score requirement (10.0.2.13 indun_zones.gear_score)
        if (dungeonZone.GearScore > 0 && character.GearScore < dungeonZone.GearScore)
        {
            Logger.Warn($"Requesting instance gear score too low ({character.GearScore} < {dungeonZone.GearScore}), characterId: {character.Id}, zoneGroupId: {dungeonZone.ZoneGroupId}");
            character.SendErrorMessage(ErrorMessageType.NotEnoughGearScore);
            return false;
        }
        
        // Check party status
        if (dungeonZone.PartyOnly && team == null)
        {
            Logger.Warn($"Requesting instance team required, characterId: {character.Id}, zoneGroupId: {dungeonZone.ZoneGroupId}");
            character.SendErrorMessage(ErrorMessageType.NeedParty);
            return false;
        }
        
        // 10.0.2.13: indun_zones.item_id removed; the item-requirement check was dead (ItemId always 0)

        return true;
    }

    /// <summary>
    /// Revalidates the content-authored schedule and permission tags at the point a player enters.
    /// Matchmaking calls this again after an invitation because either condition can change while queued.
    /// </summary>
    public bool VerifyDungeonAdmission(IndunZone dungeonZone, Character character)
    {
        var utcNow = AdmissionUtcNow();
        var entranceNow = dungeonZone.UseUtcEntranceTimes
            ? utcNow
            : TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.Local);
        if (!InstanceAdmissionRules.IsOpen(dungeonZone.EntranceTimes, entranceNow))
        {
            Logger.Warn(
                "Requesting closed instance, characterId: {0}, zoneGroupId: {1}, instanceId: {2}, time: {3:O}, useUtc: {4}",
                character.Id, dungeonZone.ZoneGroupId, dungeonZone.InstanceCatalogId, entranceNow,
                dungeonZone.UseUtcEntranceTimes);
            character.SendErrorMessage(ErrorMessageType.TryLaterInstance);
            return false;
        }

        var tagFailure = InstanceAdmissionRules.CheckTags(
            dungeonZone.PermissionTags,
            dungeonZone.PermissionWhiteListBit,
            (kind, tagId) => AdmissionTagMatcher(character, kind, tagId));
        if (tagFailure != InstanceAdmissionFailure.None)
        {
            Logger.Warn(
                "Requesting instance with invalid permission tags ({0}), characterId: {1}, zoneGroupId: {2}, instanceId: {3}",
                tagFailure, character.Id, dungeonZone.ZoneGroupId, dungeonZone.InstanceCatalogId);
            character.SendErrorMessage(ErrorMessageType.ProhibitedInInstance);
            return false;
        }

        return true;
    }

    private static bool CharacterHasPermissionTag(
        Character character,
        InstancePermissionTagKind kind,
        uint tagId)
    {
        return kind switch
        {
            InstancePermissionTagKind.Buff => character.Buffs?.CheckBuffTag(tagId) == true,
            _ => false
        };
    }

    /// <summary>
    /// Returns true when the dungeon already holds its maximum allowed number of players.
    /// Kept as a single decision point so the manager and <see cref="Dungeon.IsFull"/> stay in sync.
    /// </summary>
    private static bool IsDungeonFull(int characterCount, uint maxPlayers) => characterCount >= maxPlayers;

    /// <summary>
    /// Creates a new player created dungeon instance
    /// </summary>
    /// <param name="dungeonZone"></param>
    /// <param name="character"></param>
    /// <param name="channelId"></param>
    /// <param name="dungeon"></param>
    /// <returns></returns>
    /// <summary>
    /// Builds an instance copy for a matched group without putting anyone in it yet, and grants its
    /// members access so their later enter joins this copy instead of creating another one.
    /// </summary>
    /// <returns>The copy being built, or null when one could not be started.</returns>
    public Dungeon PrepareMatchInstance(uint zoneId, Character owner, IReadOnlyList<uint> memberCharacterIds)
    {
        if (owner == null || memberCharacterIds == null || memberCharacterIds.Count == 0)
            return null;

        var targetZone = zoneManager.GetZoneById(zoneId);
        if (targetZone == null)
            return null;

        var dungeonZone = IndunGameData.Instance.GetDungeonZone(targetZone.GroupId);
        if (dungeonZone == null)
            return null;

        if (worldManager.GetWorlds().Length > AppConfiguration.Instance.World.MaxInstances)
        {
            Logger.Warn($"Preparing a match instance would exceed the allowed amount, zoneGroupId: {dungeonZone.ZoneGroupId}");
            return null;
        }

        var worldTemplate = worldManager.GetWorldTemplateByZoneKey(targetZone.ZoneKey);
        var templateName = worldTemplate?.Name;
        var warmWorld = !string.IsNullOrWhiteSpace(templateName)
            ? WorldIntegration.TryClaimWarmDungeonWorld?.Invoke(templateName, owner.Id)
            : null;

        var dungeon = warmWorld != null
            ? new Dungeon(dungeonZone, owner, 0, null, warmWorld)
            : new Dungeon(dungeonZone, owner, 0, null);

        foreach (var memberId in memberCharacterIds)
            dungeon.PlayersWithAccess.Add(memberId);

        Logger.Info(
            $"Preparing match instance zoneGroupId: {dungeonZone.ZoneGroupId}, world: {dungeon.World?.Id}, members: {memberCharacterIds.Count}, warm: {warmWorld != null}");
        return dungeon;
    }

    private bool CreateDungeonInstance(IndunZone dungeonZone, Character character, uint channelId, out Dungeon dungeon)
    {
        dungeon = null;

        // Check if we have capacity
        if (worldManager.GetWorlds().Length > AppConfiguration.Instance.World.MaxInstances)
        {
            Logger.Warn($"Requesting a new instance would exceeds the allowed ammount, characterId: {character.Id}, zoneGroupId: {dungeonZone.ZoneGroupId}");
            character.SendErrorMessage(ErrorMessageType.NoServerInstanceResource);
            return false;
        }

        var team = teamManager.GetTeamByObjId(character.ObjId);
        Logger.Info($"Requesting instance, characterId: {character.Id}, zoneGroupId: {dungeonZone.ZoneGroupId}");

        // Check requirements such as level, item, etc
        if (!VerifyDungeonEnterRequirements(dungeonZone, character, team))
        {
            return false;
        }

        if (!CanCreateAfterRestoreCooldown(character.Id, dungeonZone))
        {
            character.SendErrorMessage(ErrorMessageType.TryLaterInstance);
            return false;
        }

        // Prefer a warm ZoneHost copy when configured for this world template.
        var zoneKeys = zoneManager.GetZoneKeysInZoneGroupById(dungeonZone.ZoneGroupId);
        var worldTemplate = zoneKeys.Count > 0
            ? worldManager.GetWorldTemplateByZoneKey(zoneKeys[0])
            : null;
        var templateName = worldTemplate?.Name;
        var bindOwnerId = team?.Id ?? character.Id;
        var warmWorld = !string.IsNullOrWhiteSpace(templateName)
            ? WorldIntegration.TryClaimWarmDungeonWorld?.Invoke(templateName, bindOwnerId)
            : null;

        dungeon = warmWorld != null
            ? new Dungeon(dungeonZone, character, channelId, team, warmWorld)
            : new Dungeon(dungeonZone, character, channelId, team);

        // Add creator to queue while dungeon is loading
        if (!dungeon.QueuePlayer(character))
            return false;

        RecordCreateTime(character.Id, dungeonZone.ZoneGroupId);
        return true;
    }

    /// <summary>
    /// Creates and returns a system instance with a given channel
    /// </summary>
    /// <param name="character"></param>
    /// <param name="zoneKey"></param>
    /// <param name="channelId"></param>
    /// <param name="overrideInstanceId"></param>
    /// <param name="fixedInstanceId"></param>
    /// <returns></returns>
    public Dungeon CreateSystemInstance(Character character, uint zoneKey, uint channelId, bool overrideInstanceId = false, uint fixedInstanceId = 0)
    {
        Logger.Info($"Requesting system instance, zoneKey: {zoneKey}, character: {character?.Name ?? "[SYSTEM]"}, channel: {channelId}, override InstanceId: {(overrideInstanceId ? fixedInstanceId.ToString() : "NO")}");

        var team = character != null ? teamManager.GetTeamByObjId(character.ObjId) : null;

        var dungeonZone = IndunGameData.Instance.GetDungeonZone(zoneManager.GetZoneByKey(zoneKey).GroupId);
        if (dungeonZone == null)
        {
            Logger.Error($"Requesting invalid system instance: , zoneKey: {zoneKey}, character: {character?.Name ?? "[SYSTEM]"}, channel: {channelId}, override InstanceId: {(overrideInstanceId ? fixedInstanceId.ToString() : "NO")}");
            return null;
        }
        
        // Check for duplicate system instances
        foreach (var worldInstance in worldManager.GetWorlds())
        {
            if (worldInstance.ChannelId == channelId &&
                worldInstance.DungeonInstance?.GetZoneGroupId == dungeonZone.ZoneGroupId)
            {
                // Check requirements such as level, item, etc
                if (character != null && VerifyDungeonEnterRequirements(dungeonZone, character, team))
                {
                    worldInstance.DungeonInstance.QueuePlayer(character);
                }
                return worldInstance.DungeonInstance;
            }
        }

        // Create new system instance
        var dungeon = new Dungeon(dungeonZone, character, channelId, team, overrideInstanceId, fixedInstanceId)
        {
            IsSystem = true
        };

        // Check if zones match
        if (dungeonZone.ZoneGroupId != zoneManager.GetZoneByKey(zoneKey)?.GroupId)
        {
            Logger.Info("[IndunManager] system dungeon request on different area.");
            character?.SendErrorMessage(ErrorMessageType.ProhibitedInInstance);
            return null;
        }

        // Check requirements such as level, item, etc
        if (character != null && VerifyDungeonEnterRequirements(dungeon._indunZone, character, team))
        {
            dungeon.QueuePlayer(character);
        }

        return dungeon;
    }

    /// <summary>
    /// Player requesting to remove dungeon with a given zone
    /// </summary>
    /// <param name="character"></param>
    /// <param name="zone"></param>
    /// <returns></returns>
    public bool RequestDeletion(Character character, Zone zone)
    {
        if (character == null)
        {
            return false;
        }
        if (zone == null)
        {
            character.SendErrorMessage(ErrorMessageType.AlreadyUnboundInstance);
            return false;
        }

        var removedCount = 0;
        var dungeons = GetExistingDungeonsByZoneKey(zone.ZoneKey);
        foreach (var dungeon in dungeons)
        {
            if (dungeon.IsSystem)
                continue;

            if (Dungeon.ShouldRefuseResetWhileInside(dungeon.World?.HasCharacter(character.Id) == true))
            {
                character.SendErrorMessage(ErrorMessageType.ProhibitedInInstance);
                return false;
            }

            if (!dungeon.PlayersWithAccess.Contains(character.Id))
                continue;

            // Remove player's own access flag
            dungeon.PlayersWithAccess.Remove(character.Id);
            removedCount++;

            // Portal reset (初期화) dismisses this bind so a fresh copy can be created
            // immediately — restore_item_time only gates creates while a prior create is still "held".
            ClearCreateCooldown(character.Id, dungeon.GetZoneGroupId);

            // If nobody has access anymore, remove the dungeon
            if (Dungeon.ShouldDestroyAfterLastAccessRemoved(dungeon.PlayersWithAccess.Count))
            {
                dungeon.DestroyDungeon();
            }
        }

        if (removedCount <= 0)
        {
            character.SendErrorMessage(ErrorMessageType.AlreadyUnboundInstance);
        }
        else
        {
            character.SendErrorMessage(ErrorMessageType.DismissIndunSuccessed);
        }
        return true;
    }

    /// <summary>
    /// Player requesting to leave the dungeon/instance 
    /// </summary>
    /// <param name="character"></param>
    /// <returns></returns>
    public bool RequestLeaveInstance(Character character)
    {
        if (character == null)
            return false;

        // The current copy is authoritative. Searching every world by character id can let a stale
        // membership entry route an exit (and its completion hooks) through the wrong dungeon.
        var world = character.ParentWorld;
        if (world?.DungeonInstance == null || !world.HasCharacter(character.Id))
            return false;

        character.Events.OnDungeonLeave(world, new OnDungeonLeaveArgs { Player = character });
        return true;
    }

    public void DoIndunActions(uint startActionId, WorldInstance worldInstance)
    {
        // 18 of the 263 indun_events rows have no start_action_id and used to NRE here. Every
        // next_action_id in content resolves and none cycles; the visited set only stops a bad row.
        var visited = new HashSet<uint>();
        var actionId = startActionId;
        while (actionId != 0 && visited.Add(actionId))
        {
            var action = IndunGameData.Instance.GetIndunActionById(actionId);
            if (action == null)
            {
                Logger.Debug($"DoIndunActions: world={worldInstance?.Id}, action {actionId} is not loaded");
                return;
            }

            action.Execute(worldInstance);
            Logger.Warn($"DoIndunActions: world={worldInstance?.Id}, action.Id={action.Id}, action.NextActionId={action.NextActionId}");
            actionId = action.NextActionId;
        }
    }

    /// <summary>
    /// Fans one zone-score change out to the copy's authored <c>indun_event_zone_score_level_changeds</c>
    /// rows. The copy's own score runtime is the only thing that knows a level moved, so it calls this with
    /// the change it applied; every row of the copy's zone group that the move satisfies runs its chain.
    /// A move that changes no level runs nothing.
    /// </summary>
    public void DoZoneScoreLevelChangedEvents(WorldInstance worldInstance, ZoneScoreApplication application)
    {
        var dungeon = worldInstance?.DungeonInstance;
        if (dungeon == null || !application.LevelChanged)
            return;

        var zoneGroupId = dungeon.GetZoneGroupId;
        foreach (var indunEvent in IndunGameData.Instance.GetIndunEvents(zoneGroupId))
        {
            if (indunEvent is not IndunEventZoneScoreLevelChangeds scoreEvent || !scoreEvent.Matches(application))
                continue;

            Logger.Debug(
                "IndunEventZoneScoreLevelChanged {0}: kind {1} level {2}->{3} (way {4}) in world {5}",
                scoreEvent.Id, application.KindId, application.PreviousLevel, application.Level,
                scoreEvent.ChangeWay, worldInstance.Id);
            DoIndunActions(scoreEvent.StartActionId, worldInstance);
        }
    }

    /// <summary>H-window difficulty picks (CSSelectInstanceDifficultPacket) made outside a copy, by character id.</summary>
    private Dictionary<uint, byte> SelectedDifficult { get; } = [];

    public void RememberSelectedDifficult(uint characterId, byte difficult)
    {
        lock (_lock)
            SelectedDifficult[characterId] = difficult;
    }

    /// <summary>Hands the pending pick to the copy the character enters, once.</summary>
    public bool TryTakeSelectedDifficult(uint characterId, out byte difficult)
    {
        lock (_lock)
            return SelectedDifficult.Remove(characterId, out difficult);
    }

    public bool CheckEntryAttemptCount(uint characterId, uint zoneGroupId, IndunZone indunZone, bool addAsNewEnty)
    {
        lock (_lock)
        {
            if (!EntryHistory.ContainsKey(characterId))
                EntryHistory.Add(characterId, []);

            var zoneAndEntries = EntryHistory.GetValueOrDefault(characterId);

            if (!zoneAndEntries.ContainsKey(zoneGroupId))
                zoneAndEntries.Add(zoneGroupId, []);

            var entriesList = zoneAndEntries.GetValueOrDefault(zoneGroupId);
            var now = ServerCalendar.UtcNow;
            var usedToday = IndunEntryRules.CountEntriesInDailyWindow(entriesList, now);
            var permitBonus = GetPermitBonusUnlocked(characterId, zoneGroupId);
            var permitted = IndunEntryRules.EffectivePermittedCount(indunZone.EnterCount, permitBonus);

            if (usedToday >= permitted)
            {
                Logger.Warn(
                    $"Requesting instance too many daily entries ({usedToday} / {permitted}), characterId: {characterId}, zoneGroupId: {zoneGroupId}");
                return false;
            }

            if (addAsNewEnty)
            {
                entriesList.Add(now);
                Logger.Warn($"Added entry for player {characterId} in zone {zoneGroupId}, Count is now {entriesList.Count}");
            }

            return true;
        }
    }

    /// <summary>
    /// Blocks a new copy when <see cref="IndunZone.RestoreItemTime"/> has not elapsed since the last create.
    /// </summary>
    private bool CanCreateAfterRestoreCooldown(uint characterId, IndunZone indunZone)
    {
        lock (_lock)
        {
            if (!CreateHistory.TryGetValue(characterId, out var byZone))
                return true;
            if (!byZone.TryGetValue(indunZone.ZoneGroupId, out var lastCreate))
                return true;

            var now = ServerCalendar.UtcNow;
            if (!IndunEntryRules.IsCreateOnCooldown(lastCreate, now, indunZone.RestoreItemTime))
                return true;

            Logger.Warn(
                $"Instance create on restore cooldown ({indunZone.RestoreItemTime}s), characterId: {characterId}, zoneGroupId: {indunZone.ZoneGroupId}");
            return false;
        }
    }

    private void RecordCreateTime(uint characterId, uint zoneGroupId)
    {
        lock (_lock)
        {
            if (!CreateHistory.ContainsKey(characterId))
                CreateHistory.Add(characterId, []);
            CreateHistory[characterId][zoneGroupId] = ServerCalendar.UtcNow;
        }
    }

    /// <summary>
    /// Portal reset (G / 초기화) unbinds the copy — clear <c>restore_item_time</c> so F can create again.
    /// </summary>
    private void ClearCreateCooldown(uint characterId, uint zoneGroupId)
    {
        lock (_lock)
        {
            if (!CreateHistory.TryGetValue(characterId, out var byZone))
                return;
            if (!byZone.Remove(zoneGroupId))
                return;
            Logger.Info(
                "Cleared instance create cooldown after reset, characterId: {0}, zoneGroupId: {1}",
                characterId, zoneGroupId);
        }
    }

    /// <summary>Per-dungeon visit rows for <see cref="AAEmu.Game.Core.Packets.G2C.SCInstanceVisitCountsPacket"/>.</summary>
    public List<InstanceVisitCountRecord> GetVisitCountRecords(uint characterId)
    {
        var now = ServerCalendar.UtcNow;
        var records = new List<InstanceVisitCountRecord>();
        lock (_lock)
        {
            EntryHistory.TryGetValue(characterId, out var zoneAndEntries);
            foreach (var zone in IndunGameData.Instance.GetAllDungeonZones())
            {
                if (zone.InstanceCatalogId == 0 && zone.EnterCount >= 1000)
                    continue;

                var used = 0;
                if (zoneAndEntries != null && zoneAndEntries.TryGetValue(zone.ZoneGroupId, out var entries))
                    used = IndunEntryRules.CountEntriesInDailyWindow(entries, now);

                var resetCount = GetResetPurchasesUnlocked(characterId, zone.ZoneGroupId);
                var permitBonus = GetPermitBonusUnlocked(characterId, zone.ZoneGroupId);
                records.Add(new InstanceVisitCountRecord(
                    ZoneGroupId: (int)zone.ZoneGroupId,
                    InstanceCatalogId: zone.InstanceCatalogId,
                    UsedCount: used,
                    ResetCount: resetCount,
                    PermittedCount: IndunEntryRules.EffectivePermittedCount(zone.EnterCount, permitBonus)));
            }
        }

        return records;
    }

    /// <summary>
    /// CS AddInstanceVisitCount — consume RESET/PERMIT ticket and push SCInstanceVisitCountChange.
    /// </summary>
    public bool TryAddInstanceVisitCount(Character character, sbyte visitType, int typeValue, short typeValue2)
    {
        if (character == null)
            return false;

        var zone = ResolveZoneForVisitTicket((uint)typeValue, typeValue2);
        if (zone == null)
        {
            Logger.Warn(
                "AddInstanceVisitCount: no IndunZone for type={0} type2={1} character={2}",
                typeValue, typeValue2, character.Id);
            character.SendErrorMessage(ErrorMessageType.InternalError);
            return false;
        }

        return visitType switch
        {
            IndunEntryRules.VisitTypeReset => TryBuyResetTicket(character, zone),
            IndunEntryRules.VisitTypePermit => TryBuyPermitTicket(character, zone),
            _ => FailUnknownVisitType(character, visitType)
        };
    }

    private static bool FailUnknownVisitType(Character character, sbyte visitType)
    {
        Logger.Warn("AddInstanceVisitCount: unknown visitType={0} character={1}", visitType, character.Id);
        character.SendErrorMessage(ErrorMessageType.InternalError);
        return false;
    }

    private IndunZone ResolveZoneForVisitTicket(uint catalogOrZero, short typeValue2)
    {
        if (catalogOrZero != 0)
            return IndunGameData.Instance.GetDungeonZoneByCatalogId(catalogOrZero);

        // Client secondary key when type dword is 0: zone_group_id as u16.
        if (typeValue2 > 0)
            return IndunGameData.Instance.GetDungeonZone((uint)typeValue2);

        return null;
    }

    private bool TryBuyResetTicket(Character character, IndunZone zone)
    {
        if (zone.ResetItemId == 0)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            return false;
        }

        int resetCount;
        int cost;
        lock (_lock)
        {
            resetCount = GetResetPurchasesUnlocked(character.Id, zone.ZoneGroupId);
            if (!IndunEntryRules.CanBuyReset(resetCount, zone.ResetLimit))
            {
                character.SendErrorMessage(ErrorMessageType.InstanceVisitLimit);
                return false;
            }

            cost = IndunEntryRules.ResetTicketCost(resetCount, zone.ResetItemIncreaseScale);
        }

        var consumed = character.Inventory.Bag.ConsumeItem(
            Models.Game.Items.Actions.ItemTaskType.ConsumeIndunTicket,
            zone.ResetItemId,
            cost,
            null);
        if (consumed < cost)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            return false;
        }

        InstanceVisitCountRecord row;
        lock (_lock)
        {
            if (!ResetPurchaseCount.ContainsKey(character.Id))
                ResetPurchaseCount[character.Id] = [];
            ResetPurchaseCount[character.Id][zone.ZoneGroupId] = resetCount + 1;

            // RESET clears today's used entries for this zone group (visit count refresh).
            if (EntryHistory.TryGetValue(character.Id, out var byZone) &&
                byZone.TryGetValue(zone.ZoneGroupId, out var entries))
            {
                var dayStart = IndunEntryRules.DailyWindowStartUtc(ServerCalendar.UtcNow);
                entries.RemoveAll(t =>
                {
                    var utc = t.Kind switch
                    {
                        DateTimeKind.Utc => t,
                        DateTimeKind.Local => t.ToUniversalTime(),
                        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc)
                    };
                    return utc >= dayStart;
                });
            }

            row = BuildVisitRowUnlocked(character.Id, zone, ServerCalendar.UtcNow);
        }

        character.SendPacket(new Core.Packets.G2C.SCInstanceVisitCountChangePacket(row));
        Logger.Info(
            "AddInstanceVisitCount RESET character={0} zoneGroup={1} cost={2} resetCount={3}",
            character.Id, zone.ZoneGroupId, cost, row.ResetCount);
        return true;
    }

    private bool TryBuyPermitTicket(Character character, IndunZone zone)
    {
        if (zone.PermitEnterCountItemId == 0)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            return false;
        }

        var consumed = character.Inventory.Bag.ConsumeItem(
            Models.Game.Items.Actions.ItemTaskType.ConsumeIndunTicket,
            zone.PermitEnterCountItemId,
            1,
            null);
        if (consumed < 1)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            return false;
        }

        InstanceVisitCountRecord row;
        lock (_lock)
        {
            if (!PermitBonusCount.ContainsKey(character.Id))
                PermitBonusCount[character.Id] = [];
            PermitBonusCount[character.Id].TryGetValue(zone.ZoneGroupId, out var bonus);
            PermitBonusCount[character.Id][zone.ZoneGroupId] = bonus + 1;
            row = BuildVisitRowUnlocked(character.Id, zone, ServerCalendar.UtcNow);
        }

        character.SendPacket(new Core.Packets.G2C.SCInstanceVisitCountChangePacket(row));
        Logger.Info(
            "AddInstanceVisitCount PERMIT character={0} zoneGroup={1} permitted={2}",
            character.Id, zone.ZoneGroupId, row.PermittedCount);
        return true;
    }

    private InstanceVisitCountRecord BuildVisitRowUnlocked(uint characterId, IndunZone zone, DateTime now)
    {
        var used = 0;
        if (EntryHistory.TryGetValue(characterId, out var zoneAndEntries) &&
            zoneAndEntries.TryGetValue(zone.ZoneGroupId, out var entries))
            used = IndunEntryRules.CountEntriesInDailyWindow(entries, now);

        return new InstanceVisitCountRecord(
            ZoneGroupId: (int)zone.ZoneGroupId,
            InstanceCatalogId: zone.InstanceCatalogId,
            UsedCount: used,
            ResetCount: GetResetPurchasesUnlocked(characterId, zone.ZoneGroupId),
            PermittedCount: IndunEntryRules.EffectivePermittedCount(
                zone.EnterCount, GetPermitBonusUnlocked(characterId, zone.ZoneGroupId)));
    }

    private int GetResetPurchasesUnlocked(uint characterId, uint zoneGroupId)
    {
        if (!ResetPurchaseCount.TryGetValue(characterId, out var byZone))
            return 0;
        return byZone.GetValueOrDefault(zoneGroupId);
    }

    private int GetPermitBonusUnlocked(uint characterId, uint zoneGroupId)
    {
        if (!PermitBonusCount.TryGetValue(characterId, out var byZone))
            return 0;
        return byZone.GetValueOrDefault(zoneGroupId);
    }

    /// <summary>
    /// GM/ops: forget a character's daily dungeon entry history, so the visit cap stops blocking
    /// (the gate behind <c>ErrorMessageType.InstanceVisitLimit</c>).
    /// </summary>
    /// <param name="characterId">Whose history to drop.</param>
    /// <param name="zoneGroupId">A single zone group, or 0 to clear every group for that character.</param>
    /// <returns>How many entry timestamps were dropped.</returns>
    public int ClearEntryHistory(uint characterId, uint zoneGroupId)
    {
        lock (_lock)
        {
            if (!EntryHistory.TryGetValue(characterId, out var byZone))
                return 0;

            if (zoneGroupId != 0)
            {
                var dropped = byZone.TryGetValue(zoneGroupId, out var one) ? one.RemoveAll(_ => true) : 0;
                Logger.Info("ClearEntryHistory character={0} zoneGroup={1} dropped={2}",
                    characterId, zoneGroupId, dropped);
                return dropped;
            }

            var total = 0;
            foreach (var entries in byZone.Values)
                total += entries.Count;
            byZone.Clear();
            Logger.Info("ClearEntryHistory character={0} all zone groups, dropped={1}", characterId, total);
            return total;
        }
    }

    private void InfoAttempt()
    {
        lock (_lock)
        {
            if (EntryHistory is { Count: > 0 })
            {
                foreach (var (characterId, zoneAndEntries) in EntryHistory)
                {
                    foreach (var (zoneGroupId, entriesList) in zoneAndEntries)
                    {
                        Logger.Debug($"For player={characterId} ({worldManager.GetCharacterById(characterId)?.Name}): {entriesList.Count} entries into dungeon zone group {zoneGroupId} ({zoneManager.GetZoneGroupById(zoneGroupId)?.Name})");
                    }
                }
            }
        }
    }
}
