using System.Collections.Concurrent;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun.Events;
using AAEmu.Game.Models.Game.Indun.Matching;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Indun;

public class Dungeon : IPreparedIndunInstance
{
    // ReSharper disable once InconsistentNaming
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// List of players who have been granted access to this dungeon (and did not reset it yet)
    /// </summary>
    public HashSet<uint> PlayersWithAccess { get; init; } = [];

    /// <summary>
    /// Players who already paid a daily entry for this copy. Rejoin after leave must not charge again.
    /// </summary>
    private readonly HashSet<uint> _chargedEntryIds = [];

    /// <summary>
    /// The actual linked world instance
    /// </summary>
    public WorldInstance World { get; set; }
    private readonly ZoneInstanceId _zoneInstanceId;
    public readonly IndunZone _indunZone;
    // unused private List<Character> _teleportList;
    private readonly ConcurrentDictionary<uint, DateTime> _leaveRequests;
    private Character _characterOwner;
    private Team.Team _ownerTeam;
    /// <summary>
    /// Holds the list of players that wants to enter this dungeon while it's being created
    /// </summary>
    public HashSet<Character> EnterRequests { get; } = [];
    private bool _isTeamOwned;
    private readonly Dictionary<uint, bool> _rooms;
    /// <summary>Round counter for zone groups with indun_rounds rows (125, 126, 130); inert (TotalRounds 0) elsewhere.</summary>
    public IndunRoundState Rounds { get; }
    /// <summary>The H-window difficulty applied to this copy; null until a pick reaches it.</summary>
    public byte? Difficult { get; private set; }
    private readonly IndunDifficultySelectionState _difficultySelection = new();
    /// <summary>Raised once per copy, on the NextRound that clears its last round. The reward path hangs here.</summary>
    public static event Action<Dungeon> DungeonCompleted;
    //private static Dictionary<uint, Dictionary<uint, int>> _attempts; // <ownerId, <zoneGroupId, attempts>> - dungeon attempts used
    //private const int FreeAttempts = 3;  // free attempts
    //private const int ExtraAttempts = 2; // additional attempts
    //public bool IsWaitingDungeonAccessAttemptsCleared { get; set; }

    // ReSharper disable once ChangeFieldTypeToSystemThreadingLock
    private readonly object _lock = new();
    private static readonly string _liveRewardRunEpoch = $"{Environment.ProcessId}:{DateTime.UtcNow.Ticks}";
    private static long _liveRewardRunSequence;

    public bool IsTeamOwned { get => _isTeamOwned; }
    public Character GetCharacterOwner { get => _characterOwner; }
    public Team.Team GetOwnerTeam { get => _ownerTeam; }
    public uint GetZoneGroupId { get => _indunZone.ZoneGroupId; }
    public uint GetInstanceCatalogId { get => _indunZone.InstanceCatalogId; }

    /// <summary>
    /// Logical reward-run identity. The default is a live-copy key and is intentionally not a
    /// process-restart recovery mechanism; a caller that rehydrates a dungeon must pass a persisted
    /// rewardRunId to the constructor.
    /// </summary>
    public string RewardRunId { get; } = string.Empty;
    public bool IsSystem { get; init; }
    public bool FinishedLoading { get; set; }
    private readonly DateTime _createTime = DateTime.UtcNow;

    /// <summary>
    /// For system dungeons like the mirage and the library
    /// </summary>
    /// <param name="indunZone"></param>
    /// <param name="character"></param>
    /// <param name="team"></param>
    /// <param name="overrideInstanceId"></param>
    /// <param name="fixedInstanceId"></param>
    /// <param name="channelId"></param>
    /// <param name="rewardRunId">Persisted logical run identity for rehydrated copies; null uses the live-only fallback.</param>
    public Dungeon(IndunZone indunZone, Character character, uint channelId, Team.Team team, bool overrideInstanceId = false, uint fixedInstanceId = 0, string rewardRunId = null)
        : this(indunZone, character, channelId, team, existingWorld: null, overrideInstanceId, fixedInstanceId, rewardRunId)
    {
    }

    /// <summary>
    /// Attach a dungeon to a pre-warmed <see cref="WorldInstance"/> (warm ZoneHost pool claim).
    /// </summary>
    public Dungeon(IndunZone indunZone, Character character, uint channelId, Team.Team team, WorldInstance existingWorld, string rewardRunId = null)
        : this(indunZone, character, channelId, team, existingWorld, overrideInstanceId: false, fixedInstanceId: 0, rewardRunId)
    {
    }

    private Dungeon(
        IndunZone indunZone,
        Character character,
        uint channelId,
        Team.Team team,
        WorldInstance existingWorld,
        bool overrideInstanceId,
        uint fixedInstanceId,
        string rewardRunId)
    {
        _indunZone = indunZone;
        _leaveRequests = new ConcurrentDictionary<uint, DateTime>();
        _rooms = [];
        Rounds = new IndunRoundState(IndunGameData.Instance.GetRounds(indunZone.ZoneGroupId));

        _isTeamOwned = team != null;
        _ownerTeam = team;
        _characterOwner = character;

        var zoneKeys = ZoneManager.Instance.GetZoneKeysInZoneGroupById(_indunZone.ZoneGroupId);
        switch (zoneKeys.Count)
        {
            case > 1:
                {
                    Logger.Info("There are more than one zone keys for this dungeon?!");
                    break;
                }
            case 0:
                {
                    Logger.Error("No Zone Keys found for this zone group id.");
                    return;
                }
        }
        var worldTemplate = WorldManager.Instance.GetWorldTemplateByZoneKey(zoneKeys[0]);

        Logger.Info($"[Dungeon] Create system dungeon {worldTemplate?.Name} - channel {channelId}...");
        if (existingWorld != null)
        {
            World = existingWorld;
        }
        else
        {
            // Do not pass the player as notifyPlayer: CreateWorldInstance would send a second
            // "creating dungeon" dialog before QueuePlayer runs.
            World = WorldManager.Instance.CreateWorldInstance(worldTemplate, channelId, overrideInstanceId, fixedInstanceId, notifyPlayer: null);
        }

        World.DungeonInstance = this;
        RewardRunId = string.IsNullOrWhiteSpace(rewardRunId)
            ? $"live:{_liveRewardRunEpoch}:{Interlocked.Increment(ref _liveRewardRunSequence)}:{World.Id}:{_indunZone.InstanceCatalogId}:{_indunZone.ZoneGroupId}"
            : rewardRunId;
        _zoneInstanceId = new ZoneInstanceId(zoneKeys.First(), World.Id);

        // Grant access here. The manager queues the player once so the create dialog is not stacked.
        if (character != null)
            PlayersWithAccess.Add(character.Id);
        // Add team members to allow access
        if (team != null)
        {
            foreach (var teamMember in team.Members)
            {
                if (teamMember?.Character == null)
                    continue;
                PlayersWithAccess.Add(teamMember.Character.Id);
            }
        }

        TickManager.Instance.OnTick.Subscribe(AreaClearTick, TimeSpan.FromSeconds(1), true);

        RegisterIndunEvents();
        
        // Create a loading task to run the loading async
        var loadTask = new DungeonLoaderTask(worldTemplate, this, World.Id, character);
        TaskManager.Instance.Schedule(loadTask, TimeSpan.FromMilliseconds(100), null, 1);
        TickManager.Instance.OnTick.Subscribe(LeaveDungeonTick, TimeSpan.FromSeconds(5), true);
    }

    /// <summary>
    /// Returns true if the dungeon is full capacity, false if not.
    /// </summary>
    public bool IsFull => (World?.GetCharacterCount() ?? 0) >= _indunZone.MaxPlayers;

    /// <summary>
    /// Returns true if the dungeon has players inside, false if not.
    /// </summary>
    private bool HasPlayers => (World?.GetCharacterCount() ?? 0) > 0;

    public bool IsExpired { get => !IsSystem && _createTime.AddDays(1) < DateTime.UtcNow; }

    /// <inheritdoc />
    public bool IsReady => FinishedLoading;

    /// <inheritdoc />
    public void Discard()
    {
        // A copy still waiting for its host tears itself down if the host never arrives, and one that
        // players already reached belongs to them. Only a finished, empty copy is ours to remove.
        if (!FinishedLoading || HasPlayers || EnterRequests.Count > 0)
            return;

        DestroyDungeon();
    }

    /// <summary>
    /// Adds a player to the queue while the dungeon is still loading
    /// </summary>
    /// <param name="character"></param>
    public bool QueuePlayer(Character character)
    {
        if (!CanQueuePlayer(character))
        {
            if (character != null)
            {
                Logger.Info($"[{World}] Player {character.Name} did too many dungeon attempts.");
                character.SendErrorMessage(ErrorMessageType.InstanceVisitLimit);
            }
            return false;
        }

        if (EnterRequests.Contains(character))
        {
            character.SendPacket(new SCProcessingInstancePacket((int)_zoneInstanceId.ZoneId));
            return true;
        }
        
        if (ShouldChargeDailyEntry(_chargedEntryIds.Contains(character.Id)))
        {
            if (!IndunManager.Instance.CheckEntryAttemptCount(character.Id, GetZoneGroupId, _indunZone, true))
            {
                Logger.Info($"[{World}] Player {character.Name} did too many dungeon attempts.");
                character.SendErrorMessage(ErrorMessageType.InstanceVisitLimit);
                return false;
            }

            _chargedEntryIds.Add(character.Id);
        }

        PlayersWithAccess.Add(character.Id);
        if (FinishedLoading)
        {
            AddPlayer(character);
        }
        else
        {
            character.SendPacket(new SCProcessingInstancePacket((int)_zoneInstanceId.ZoneId));
            EnterRequests.Add(character);
        }
        return true;
    }

    /// <summary>
    /// Dry daily-entry check. Already-queued or already-charged members of this copy pass.
    /// </summary>
    public bool CanQueuePlayer(Character character)
    {
        if (character == null)
            return false;
        if (EnterRequests.Contains(character) || _chargedEntryIds.Contains(character.Id))
            return true;
        return IndunMatchEnterRules.CanAdmit(
            alreadyChargedThisCopy: false,
            dailyEntryAllowed: IndunManager.Instance.CheckEntryAttemptCount(
                character.Id, GetZoneGroupId, _indunZone, false));
    }

    /// <summary>
    /// Add player to Dungeon
    /// </summary>
    /// <param name="character"></param>
    public void AddPlayer(Character character)
    {
        Logger.Info($"[Dungeon] Adding player {character.Name} to dungeon {_zoneInstanceId.InstanceId}, {_zoneInstanceId.ZoneId}");

        // A pick made in the H-window before entering lands on the first copy this character enters.
        if (Difficult is null && IndunManager.Instance.TryTakeSelectedDifficult(character.Id, out var pickedDifficult))
            SetDifficult(pickedDifficult);

        lock (_lock)
        {
            if (!World.HasCharacter(character.Id))
            {
                World.AddObject(character);
            }
            else
            {
                Logger.Info($"[Dungeon] Player {character.Name} already exists in dungeon {_zoneInstanceId.InstanceId}, {_zoneInstanceId.ZoneId}. Most likely an error in logic?");
            }
        }

        // Force despawn all mates of the player in the old world
        character.ParentWorld?.MateManager?.RemoveAndDespawnAllActiveOwnedMates(character);

        if (IsSystem)
        {
            MoveCharacterToSystemInstance(character);
        }
        else
        {
            MoveCharacterToDungeon(character);
        }
    }

    /// <summary>
    /// Remove player from Dungeon
    /// </summary>
    /// <param name="character"></param>
    private bool RemovePlayer(Character character)
    {
        if (character == null) { return false; }
        lock (_lock)
        {
            return World.RemoveObject(character);
        }
    }

    /// <summary>
    /// Destroys dungeon instance for teams
    /// </summary>
    private async Task DestroyTeamDungeon()
    {
        await Task.Delay(5000);

        Logger.Info($"[Dungeon] instanceId={_zoneInstanceId.InstanceId}, zoneId={_zoneInstanceId.ZoneId}: Destroying team dungeon...");

        if (World == null)
        {
            return;
        }

        UnregisterIndunEvents();

        // Unregister events attached to Npcs
        var npcList = new List<Npc>();
        foreach (var region in World.Regions)
        {
            region?.GetList(npcList, 0);
        }
        foreach (var npc in npcList)
        {
            if (npc == null) { continue; }

            npc.UnregisterNpcEvents();
            //npc.Delete();
            //ObjectIdManager.Instance.ReleaseId(npc.ObjId);
        }

        WorldIntegration.StopInstanceZoneHost?.Invoke(World.Id);
        WorldManager.Instance.RemoveWorld(World.Id);
        // Despawns everything and returns the instance Id to the pool
        World.Dispose();

        World = null;
    }

    /// <summary>
    /// Destroys dungeon instance
    /// </summary>
    public bool DestroyDungeon()
    {
        Logger.Info($"[Dungeon] instanceId={_zoneInstanceId?.InstanceId}, zoneId={_zoneInstanceId?.ZoneId}: Destroying dungeon...");

        TickManager.Instance.OnTick.UnSubscribe(AreaClearTick);

        foreach (var player in World.GetAllCharacters())
        {
            _ = RemovePlayer(player);
        }

        //if (!IsOwner(character) || HasPlayers)
        //{
        //    return false;
        //}

        if (World == null)
        {
            return true;
        }

        UnregisterIndunEvents();
        TickManager.Instance.OnTick.UnSubscribe(LeaveDungeonTick);
        TickManager.Instance.OnTick.UnSubscribe(AreaClearTick);

        WorldIntegration.StopInstanceZoneHost?.Invoke(World.Id);
        WorldManager.Instance.RemoveWorld(World.Id);
        // Cleans the instance up and returns the instance Id to the pool
        World.Dispose();

        World.DungeonInstance = null;
        World = null;
        return true;
    }

    /// <summary>
    /// Moves character to instanced dungeon world
    /// </summary>
    /// <param name="character"></param>
    private void MoveCharacterToSystemInstance(Character character)
    {
        // we take the coordinates of the zone
        foreach (var wz in World.Template.XmlWorldZones.Values)
        {
            if (wz.Id == _zoneInstanceId.ZoneId)
            {
                World.Template.SpawnPosition = wz.SpawnPosition;
                break;
            }
        }
        if (World.Template.SpawnPosition != null)
        {
            character.DisabledSetPosition = true;
            RememberMainWorldReturn(character);
            character.Transform.ApplyWorldSpawnPosition(World.Template.SpawnPosition, World.Id);
            character.SendPacket(
                new SCLoadInstancePacket(
                    World.Id,
                    _zoneInstanceId.ZoneId,
                    World.Template.SpawnPosition.X,
                    World.Template.SpawnPosition.Y,
                    World.Template.SpawnPosition.Z,
                World.Template.SpawnPosition.Roll.DegToRad(),
                World.Template.SpawnPosition.Pitch.DegToRad(),
                World.Template.SpawnPosition.Yaw.DegToRad()));

            character.Events.OnDungeonLeave += OnDungeonLeave;
            character.Events.OnDisconnect += OnDisconnect;
        }
        else
        {
            Logger.Info($"World #{World.Id}, not have default spawn position.");
            character.SendErrorMessage(ErrorMessageType.NoServerInstanceResource);
        }
    }

    /// <summary>
    /// Snapshot the overworld position used when the exit portal / leave path returns the player.
    /// Matchmaking enter used to skip this, which left MainWorldPosition null and made every exit
    /// report a return-point error.
    /// </summary>
    private static void RememberMainWorldReturn(Character character)
    {
        if (character.MainWorldPosition == null ||
            character.Transform.InstanceId == WorldManager.DefaultInstanceId)
        {
            character.MainWorldPosition = character.Transform.CloneDetached(character);
        }
    }

    /// <summary>
    /// Leaves the copy into the open world. The load packet's first field is the live instance
    /// id (0 on the overworld); a world-template id here made the client load an empty Marianople.
    /// Rotation on the transform is already radians.
    /// </summary>
    private static void SendLeaveToMainWorld(Character character)
    {
        character.DisabledSetPosition = true;
        character.Transform = character.MainWorldPosition.Clone();
        character.Transform.InstanceId = WorldManager.DefaultInstanceId;
        var pos = character.MainWorldPosition.World.Position;
        var rot = character.MainWorldPosition.World.Rotation;
        character.SendPacket(new SCLoadInstancePacket(
            WorldManager.DefaultInstanceId,
            character.MainWorldPosition.ZoneId,
            pos.X, pos.Y, pos.Z,
            rot.X, rot.Y, rot.Z));
    }

    /// <summary>
    /// When enter never saved a return snapshot, fall back to the bound return-district recall.
    /// </summary>
    private static bool EnsureLeaveReturn(Character character)
    {
        if (character.MainWorldPosition != null)
            return true;

        var returnPointId = PortalManager.Instance.GetDistrictReturnPoint(
            character.ReturnDistrictId, character.Faction.Id);
        var portal = PortalManager.Instance.GetRecallById(returnPointId);
        if (portal == null)
            return false;

        character.MainWorldPosition = new Transform(
            character,
            null,
            portal.ZoneId,
            WorldManager.DefaultInstanceId,
            portal.X,
            portal.Y,
            portal.Z,
            0f,
            0f,
            portal.Yaw.DegToRad());
        return true;
    }

    /// <summary>
    /// Moves character to instanced dungeon world
    /// </summary>
    /// <param name="character"></param>
    private void MoveCharacterToDungeon(Character character)
    {
        // we take the coordinates of the zone
        foreach (var wz in World.Template.XmlWorldZones.Values)
        {
            if (wz.Id == _zoneInstanceId.ZoneId)
            {
                World.Template.SpawnPosition = wz.SpawnPosition;
                break;
            }
        }
        if (World.Template.SpawnPosition != null)
        {
            character.DisabledSetPosition = true;
            RememberMainWorldReturn(character);
            character.Transform.ApplyWorldSpawnPosition(World.Template.SpawnPosition, World.Id);
            character.SendPacket(
                new SCLoadInstancePacket(
                    World.Id,
                    _zoneInstanceId.ZoneId,
                    World.Template.SpawnPosition.X,
                    World.Template.SpawnPosition.Y,
                    World.Template.SpawnPosition.Z,
                World.Template.SpawnPosition.Roll.DegToRad(),
                World.Template.SpawnPosition.Pitch.DegToRad(),
                World.Template.SpawnPosition.Yaw.DegToRad()));

            character.Events.OnTeamJoin += OnTeamJoin;
            character.Events.OnTeamKick += OnTeamLeave;
            character.Events.OnTeamLeave += OnTeamLeave;
            character.Events.OnDungeonLeave += OnDungeonLeave;
            character.Events.OnDisconnect += OnDisconnect;
        }
        else
        {
            Logger.Info($"World #{World.Id}, does not have default spawn position.");
            character.SendErrorMessage(ErrorMessageType.NoServerInstanceResource);
        }
    }

    /// <summary>
    /// Moves player out of the instanced dungeon world.
    /// </summary>
    /// <param name="character"></param>
    private void LeaveDungeonInstance(Character character)
    {
        character.Events.OnTeamJoin -= OnTeamJoin;
        character.Events.OnTeamKick -= OnTeamLeave;
        character.Events.OnTeamLeave -= OnTeamLeave;
        character.Events.OnDungeonLeave -= OnDungeonLeave;
        character.Events.OnDisconnect -= OnDisconnect;

        _leaveRequests.TryRemove(character.Id, out _);
        _ = RemovePlayer(character);

        if (!EnsureLeaveReturn(character))
        {
            Logger.Info($"World #.{World.Id}, does not have Main World spawn position.");
            character.SendErrorMessage(ErrorMessageType.InvalidReturnPosInstance);
            return;
        }

        SendLeaveToMainWorld(character);
        // After the leave teleport: clear squad/instant-game "in match" flags so Register works.
        SquadManager.Instance.NotifyGameLeave(character);
    }

    /// <summary>
    /// Moves player out of a system instance
    /// </summary>
    /// <param name="character"></param>
    private void LeaveSystemInstance(Character character)
    {
        character.Events.OnDungeonLeave -= OnDungeonLeave;
        character.Events.OnDisconnect -= OnDisconnect;

        _leaveRequests.TryRemove(character.Id, out _);
        _ = RemovePlayer(character);

        if (!EnsureLeaveReturn(character))
        {
            Logger.Info($"World #.{World.Id}, did not have a return point set in main world for {character.Name} ({character.Id}) !");
            character.SendErrorMessage(ErrorMessageType.InvalidReturnPosInstance);
            return;
        }

        SendLeaveToMainWorld(character);
        SquadManager.Instance.NotifyGameLeave(character);
    }

    private void OnTeamJoin(object sender, OnTeamJoinArgs args)
    {
        var character = args.Player;
        var team = args.Team;
        var ownerId = team.OwnerId;
        if (character == null) { return; }

        Logger.Info($"Player {character.Name} has joined a party!");

        if (_isTeamOwned == false)
        {
            if (ownerId != _characterOwner.Id) { return; }
            _ownerTeam = team;
            _isTeamOwned = true;
            _characterOwner = null;
            Logger.Info($"[Dungeon] instanceId: {_zoneInstanceId.InstanceId}, zoneId: {_zoneInstanceId.ZoneId}. Converting solo instance into a party instance.");
            return;
        }

        if (PlayerInSameTeam(character) && !World.HasCharacter(character.Id))
        {
            World.AddObject(character);
        }
    }

    private void OnTeamLeave(object sender, OnTeamLeaveArgs args)
    {
        var teamId = args.Id;
        //var team = args.Team;
        var character = args.Player;

        if (character == null) { return; }

        Logger.Info($"Player {character.Name} has left the party {teamId}!");
        character.SendErrorMessage(ErrorMessageType.InstanceLeaveParty);
        if (World.HasCharacter(character.Id) && character.Transform.InstanceId == _zoneInstanceId.InstanceId)
        {
            PlayersWithAccess.Remove(character.Id);
            _leaveRequests.TryAdd(character.Id, DateTime.UtcNow.AddSeconds(AppConfiguration.Instance.Dungeons.AutoTeamDisbandKickTime));
        }
    }

    private void OnDungeonLeave(object sender, OnDungeonLeaveArgs args)
    {
        var character = args.Player;
        if (character == null)
        {
            return;
        }

        lock (_lock)
            _difficultySelection.Release(character.Id);

        Logger.Info($"Player {character.Name} ({character.Id}) has exited from dungeon {World}!");

        if (character.ParentWorld?.DungeonInstance == null)
            return;

        if (IsSystem)
        {
            LeaveSystemInstance(character);
            return;
        }

        LeaveDungeonInstance(character);
        // Bound copies stay up so the outside portal can re-enter or reset (초기화).
        // Destroy happens on last unbind, 24h expiry, or empty-after-party-kick.
    }

    public bool HasChargedEntry(uint characterId) => _chargedEntryIds.Contains(characterId);

    /// <summary>First visit to this copy consumes a daily; walking out and back in does not.</summary>
    internal static bool ShouldChargeDailyEntry(bool alreadyChargedThisCopy) => !alreadyChargedThisCopy;

    /// <summary>Exit doodad / last body leaving does not wipe the bound copy.</summary>
    internal static bool ShouldDestroyAfterLastPlayerLeft(bool isSystem, int remainingPlayers)
    {
        _ = isSystem;
        _ = remainingPlayers;
        return false;
    }

    /// <summary>Disconnect must not 초기화. Relog re-enters the same copy.</summary>
    internal static bool ShouldUnbindOnDisconnect() => false;

    internal static bool ShouldRefuseResetWhileInside(bool stillInThisCopy) => stillInThisCopy;

    internal static bool ShouldDestroyAfterLastAccessRemoved(int remainingAccess) => remainingAccess <= 0;

    private void OnDisconnect(object sender, OnDisconnectArgs args)
    {
        Logger.Info($"[Dungeon] instanceId={_zoneInstanceId.InstanceId}, zoneId={_zoneInstanceId.ZoneId} player={args.Player.Name} disconnected!");

        lock (_lock)
            _difficultySelection.Release(args.Player.Id);

        if (IsSystem)
        {
            _ = RemovePlayer(args.Player);
            args.Player.Events.OnDungeonLeave -= OnDungeonLeave;
            args.Player.Events.OnDisconnect -= OnDisconnect;
            return;
        }

        _ = RemovePlayer(args.Player);
        args.Player.Events.OnTeamJoin -= OnTeamJoin;
        args.Player.Events.OnTeamKick -= OnTeamLeave;
        args.Player.Events.OnTeamLeave -= OnTeamLeave;
        args.Player.Events.OnDungeonLeave -= OnDungeonLeave;
        args.Player.Events.OnDisconnect -= OnDisconnect;
    }

    /// <summary>
    /// Return true if the team Id matches to the team that owns the dungeon instance, false if not.
    /// </summary>
    /// <param name="player"></param>
    /// <returns></returns>
    public bool PlayerInSameTeam(Character player)
    {
        if (_isTeamOwned == false) { return false; }

        return _ownerTeam.Id == TeamManager.Instance.GetTeamByObjId(player.ObjId).Id;
    }

    private bool IsOwner(Character character)
    {
        return _isTeamOwned == false && _characterOwner?.Id == character?.Id;
    }

    /// <summary>
    /// After a party-leave kick timer expires, remove that player. Optionally destroy an empty copy.
    /// </summary>
    /// <param name="delta"></param>
    private void LeaveDungeonTick(TimeSpan delta)
    {
        if (_leaveRequests.IsEmpty)
            return;

        foreach (var (playerId, leaveRequestTime) in _leaveRequests.ToList())
        {
            if (DateTime.UtcNow <= leaveRequestTime) { continue; }

            var character = WorldManager.Instance.GetCharacterById(playerId);
            if (character == null)
            {
                Logger.Warn($"[{World}] zoneId={_zoneInstanceId.ZoneId}: Player Id {playerId} not found. Removing request.");
                _leaveRequests.TryRemove(playerId, out _);
                return;
            }

            if (character.InParty)
            {
                if (PlayerInSameTeam(character))
                {
                    Logger.Info($"[{World}] zoneId={_zoneInstanceId.ZoneId}: {character.Name} ({character.Id}) rejoined party, aborting.");
                    _leaveRequests.TryRemove(playerId, out _);
                    return;
                }
            }

            Logger.Info($"[{World}] zoneId={_zoneInstanceId.ZoneId}: Removing {character.Name} ({character.Id}) from instance.");
            character.Events.OnDungeonLeave(World, new OnDungeonLeaveArgs { Player = character });
            // LeaveDungeonInstance(character); // Called in OnDungeonLeave

            // QoL update that's different from retail
            // If a person got kicked and there are no more people left in the dungeon, destroy it (if it isn't a system dungeon)
            if (AppConfiguration.Instance.Dungeons.AutoCleanupAfterKick && World.GetCharacterCount() <= 0 && !IsSystem)
            {
                if (!DestroyDungeon())
                {
                    Logger.Warn($"[{World}] Failed to removed empty dungeon with no players after kick from dungeon, zoneId={_zoneInstanceId.ZoneId}");
                }
            }
        }
    }

    public void RegisterIndunEvents()
    {
        Logger.Info($"Registering Indun Events...");
        foreach (var ev in IndunGameData.Instance.GetIndunEvents(_indunZone.ZoneGroupId))
        {
            // The constructor and DungeonLoaderTask both register (the loader again once the content is
            // spawned, which IndunEventNoAliveChInRoom needs). Unsubscribe first so no handler is
            // attached twice and every event fires once per cause.
            ev?.UnSubscribe(World);
            ev?.Subscribe(World);
        }
    }

    private void UnregisterIndunEvents()
    {
        Logger.Info($"Unregistering Indun Events...");
        foreach (var ev in IndunGameData.Instance.GetIndunEvents(_indunZone.ZoneGroupId))
        {
            ev?.UnSubscribe(World);
        }
    }

    private bool IsRoomCleared(uint roomId)
    {
        return _rooms.TryGetValue(roomId, out var cleared) && cleared;
    }

    public void SetRoomCleared(uint roomId)
    {
        _rooms[roomId] = true;
    }

    /// <summary>IndunActionNextRound. Completion is granted once per copy, whatever fires it a second time.</summary>
    internal void ApplyNextRound(int roundAdd)
    {
        bool completed;
        lock (_lock)
        {
            completed = Rounds.ApplyNextRound(roundAdd);
        }

        Logger.Info($"[{World}] round {Rounds.CurrentRound}/{Rounds.TotalRounds} after +{roundAdd}{(completed ? ", completed" : string.Empty)}");
        if (completed)
            DungeonCompleted?.Invoke(this);
    }

    /// <summary>IndunActionRoundAlarm: one SCIndunRoundPlayStatusPacket (0x2DB) to every player in the copy.</summary>
    internal void RoundAlarm(byte roundAlarmKindId, bool showUi)
    {
        bool playing, success, nextRoundBoss;
        int round;
        lock (_lock)
        {
            switch (roundAlarmKindId)
            {
                case IndunRoundRules.AlarmKindStart:
                    Rounds.StartRound(DateTime.UtcNow);
                    success = false;
                    break;
                case IndunRoundRules.AlarmKindEnd:
                    success = Rounds.EndRound();
                    break;
                default:
                    Logger.Debug($"[{World}] round alarm kind {roundAlarmKindId} is not in enum_indun_round_alarm_kinds");
                    return;
            }

            playing = Rounds.Playing;
            round = Rounds.CurrentRound;
            // The counters the packet reports are the ones read under this lock: at an end alarm the
            // NextRound of the round just cleared has already moved CurrentRound, so both describe the
            // round about to be played and not the one that just ended.
            nextRoundBoss = Rounds.NextRoundIsBoss;
        }

        BroadcastToPlayers(new SCIndunRoundPlayStatusPacket(playing, success, IndunRoundRules.ToWireRound(round), nextRoundBoss, showUi));
    }

    /// <summary>SCIndunInitialRoundInfoPacket (0x2D9) on instance load, so a relog sees the live counter.</summary>
    public void SendInitialRoundInfo(Character character)
    {
        if (character == null || !Rounds.HasRounds)
            return;

        sbyte current, total;
        bool playing;
        lock (_lock)
        {
            current = IndunRoundRules.ToWireRound(Rounds.CurrentRound);
            total = IndunRoundRules.ToWireRound(Rounds.TotalRounds);
            playing = Rounds.Playing;
        }

        character.SendPacket(new SCIndunInitialRoundInfoPacket(current, total, playing));
    }

    /// <summary>In-memory fast marker; the durable W03A ledger is authoritative for a logical run.</summary>
    internal bool TryClaimMailReward(uint instanceRewardKindId)
    {
        lock (_lock)
            return Rounds.TryMarkMailReward(instanceRewardKindId);
    }

    /// <summary>H-window pick (CSSelectInstanceDifficultPacket) applied to this copy; raises IndunEventDifficultChanged.</summary>
    public bool SetDifficult(byte difficult)
    {
        var world = World;
        if (world == null)
            return false;

        var hasOptions = IndunGameData.Instance.HasDifficultyOptions(GetZoneGroupId);
        if (hasOptions && !IndunGameData.Instance.IsDifficultyAvailable(GetZoneGroupId, difficult))
            return false;

        lock (_lock)
        {
            // Difficulty drives an indun action chain. The first accepted selection owns the copy;
            // packet replay or a second player cannot run that chain, and its rewards, again.
            if (Difficult == difficult)
                return true;
            if (hasOptions && Difficult != null)
                return false;
            Difficult = difficult;
        }

        world.Events.OnIndunDifficultChanged(world, new OnIndunDifficultChangedArgs { Difficult = difficult });
        return true;
    }

    public bool BeginDifficultySelection(Character character, Action completion)
    {
        if (character == null || World == null || character.ParentWorld != World || !World.HasCharacter(character.Id))
            return false;
        lock (_lock)
        {
            return _difficultySelection.Reserve(character.Id, Difficult, completion);
        }
    }

    public bool HasDifficultySelection(Character character)
    {
        if (character == null)
            return false;
        lock (_lock)
            return character.ParentWorld == World && World?.HasCharacter(character.Id) == true &&
                   _difficultySelection.IsReservedBy(character.Id);
    }

    /// <summary>
    /// True while this copy's difficulty selection is open in someone else's hands — the authorization
    /// question behind a refused second picker.
    /// </summary>
    internal bool HasDifficultySelectionHeldByOther(Character character)
    {
        if (character == null)
            return false;
        lock (_lock)
            return _difficultySelection.IsHeld && !_difficultySelection.IsReservedBy(character.Id);
    }

    public bool SetDifficult(Character character, byte difficult)
    {
        if (character == null)
            return false;

        Action completion;
        bool changed;
        WorldInstance world;
        lock (_lock)
        {
            world = World;
            if (world == null || character.ParentWorld != world || !world.HasCharacter(character.Id))
                return false;

            var selected = Difficult;
            if (!_difficultySelection.TryApply(
                    character.Id,
                    ref selected,
                    difficult,
                    IndunGameData.Instance.IsDifficultyAvailable(GetZoneGroupId, difficult),
                    out completion,
                    out changed))
                return false;
            Difficult = selected;
        }

        if (changed)
            world.Events.OnIndunDifficultChanged(world, new OnIndunDifficultChangedArgs { Difficult = difficult });
        completion?.Invoke();
        return true;
    }

    private void BroadcastToPlayers(GamePacket packet)
    {
        var world = World;
        if (world == null)
            return;

        foreach (var character in world.GetAllCharacters())
            character?.SendPacket(packet);
    }

    public uint GetDungeonWorldId()
    {
        return World.Id;
    }

    public uint GetDungeonTemplateId()
    {
        return World.Template.Id;
    }

    private void AreaClearTick(TimeSpan delta)
    {
        lock (_lock)
        {
            foreach (var ev in IndunGameData.Instance.GetIndunEvents(_indunZone.ZoneGroupId))
            {
                if (ev is not IndunEventNoAliveChInRooms room) { continue; }

                // A cleared room must not stop the scan of the other rooms.
                if (IsRoomCleared(room.RoomId)) { continue; }

                var indunRoom = IndunGameData.Instance.GetRoom(room.RoomId);
                var doodad = room.GetRoomDoodad(World.Id);

                if (doodad == null) { continue; }

                var radiusCount = WorldManager.GetAround<Character>(doodad, indunRoom.Radius)
                    .Where(o => o.GetDistanceTo(doodad) <= indunRoom.Radius).ToList().Count;

                Logger.Info($"Character:{radiusCount} in room:{room.RoomId}");

                if (radiusCount == 0 && room.GetRoomPlayerCount(World.Id) != 0)
                {
                    IndunManager.Instance.DoIndunActions(ev.StartActionId, World);
                }

                room.SetRoomPlayerCount(World.Id, (uint)radiusCount);
            }
        }
    }
}
