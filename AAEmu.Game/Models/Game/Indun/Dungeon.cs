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
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.Skills.Effects;
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
    /// <summary>
    /// This copy's zone key (<c>ZoneKeys[0]</c>) — what the World's zone registry indexes the copy's host
    /// by, together with <c>World.Id</c>. 0 when the zone group has no zone key (the copy never loads).
    /// </summary>
    private uint _zoneKey;
    /// <summary>Set once the copy's authored <c>tower_defs</c> run has been started on its host.</summary>
    private bool _instanceScriptStarted;
    /// <summary>Throttles the "host refused the start" warning so a slow host does not fill the log.</summary>
    private bool _instanceScriptRefusedLogged;
    /// <summary>Last phase the copy logged, so each transition is reported once.</summary>
    private IndunInstancePhase _lastPhase = IndunInstancePhase.None;
    /// <summary>
    /// UTC moment the copy's first player was placed inside it; null while the copy is still empty. The
    /// copy's ready/play/end clock runs from here, not from creation, so an invite that sits unaccepted
    /// cannot burn the copy's budgets.
    /// </summary>
    private DateTime? _copyStartUtc;
    /// <summary>Characters admitted through a match whose client is waiting to be told it has joined this copy.</summary>
    private readonly HashSet<uint> _matchedEntries = [];
    /// <summary>Matched players being walked through the copy's opening, by character id.</summary>
    private readonly Dictionary<uint, IndunOpeningStage> _openings = new();

    private static readonly TimeSpan ReadoutUnitSearchInterval = TimeSpan.FromSeconds(5);
    private List<Npc> _readoutUnits;
    private DateTime _readoutUnitsSearchedAt;
    private string _lastReadoutShape;
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

    /// <summary>
    /// The copy's zone scores, one entry per <c>zone_score_kinds</c> row whose content names this zone
    /// group. A level move on it fires the zone group's authored zone-score events (see
    /// <see cref="OnZoneScoreChanged"/>); a zone group with no such kinds stays an empty table.
    /// </summary>
    public ZoneScoreRuntime ZoneScores { get; }
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
    /// <summary>The zone and world instance that identify this copy on the instant-game packets.</summary>
    public ZoneInstanceId ZoneInstance => _zoneInstanceId;

    /// <summary>
    /// Records that <paramref name="character"/> enters through a match: its client accepted this copy's
    /// invite, so once its load is done it is told it has joined rather than handed a running copy.
    /// </summary>
    public void ExpectMatchedEntry(Character character)
    {
        if (character == null)
            return;
        lock (_lock)
            _matchedEntries.Add(character.Id);
    }

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
        // The copy's own zone scores. Every level move on it is what the zone group's authored
        // indun_event_zone_score_level_changeds rows (scenery and round chains) hang on, and the copy's
        // players see each move through SCZoneScoreUpdatePacket.
        ZoneScores = new ZoneScoreRuntime(
            indunZone.ZoneGroupId, FactionScoringGameData.Instance, new MySqlZoneScoreRuntimeStore());
        ZoneScores.ScoreChanged += OnZoneScoreChanged;
        ZoneScores.Load();

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
        _zoneKey = zoneKeys.First();

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

    /// <summary>
    /// The copy's phase, from its <c>indun_zones.option</c> budget and the moment its first player arrived:
    /// ready ("wait time") → play → end → finished. A copy with nobody inside yet — or with no budget — is
    /// <see cref="IndunInstancePhase.None"/>.
    /// </summary>
    public IndunInstancePhase InstancePhase
    {
        get
        {
            if (!IndunInstancePhaseRules.TryGetClockOrigin(_copyStartUtc, out var origin))
                return IndunInstancePhase.None;
            return IndunInstancePhaseRules.PhaseAt(_indunZone.Option, origin, DateTime.UtcNow);
        }
    }

    /// <summary>Whole seconds left in the copy's current phase; 0 when it has no clock or has finished.</summary>
    public int PhaseSecondsRemaining =>
        IndunInstancePhaseRules.TryGetClockOrigin(_copyStartUtc, out var origin)
            ? IndunInstancePhaseRules.SecondsRemaining(_indunZone.Option, origin, DateTime.UtcNow)
            : 0;

    /// <summary>
    /// Drives the copy's phase clock: logs each phase change (ready → play → end → finished) and starts the
    /// copy's authored <c>tower_defs</c> run once, at the end of the ready ("wait time") window.
    /// </summary>
    private void TickInstanceScript()
    {
        var option = _indunZone.Option;
        if (!option.IsScripted)
            return;

        // Nothing is running while nobody is inside: the clock starts with the first arrival.
        if (!IndunInstancePhaseRules.TryGetClockOrigin(_copyStartUtc, out var copyStart))
            return;

        var now = DateTime.UtcNow;
        var phase = IndunInstancePhaseRules.PhaseAt(option, copyStart, now);
        if (phase != _lastPhase)
        {
            _lastPhase = phase;
            Logger.Info(
                "[{0}] instance phase → {1} ({2}s left of this phase)",
                World, phase, IndunInstancePhaseRules.SecondsRemaining(option, copyStart, now));
        }

        // Claim before calling out: a per-second tick and an arrival can both reach this at once, and the
        // run must start exactly once.
        if (_instanceScriptStarted || _zoneKey == 0 || World == null)
            return;
        if (!IndunInstancePhaseRules.ShouldStartScript(
                option, FinishedLoading, _instanceScriptStarted, copyStart, now))
            return;
        var start = WorldIntegration.StartInstanceTowerDef;
        if (start == null)
            return;

        _instanceScriptStarted = true;
        var started = start(_zoneKey, World.Id, option.TowerDefId, (ushort)_indunZone.ZoneGroupId);
        if (started)
        {
            Logger.Info(
                "[{0}] instance script: tower_def {1} started on zone {2}/{3}",
                World, option.TowerDefId, _zoneKey, World.Id);
            return;
        }

        _instanceScriptStarted = false; // the host is not loaded yet; retry on the next tick
        if (!_instanceScriptRefusedLogged)
        {
            _instanceScriptRefusedLogged = true;
            Logger.Warn(
                "[{0}] instance script: tower_def {1} refused by zone {2}/{3}; retrying until the host answers",
                World, option.TowerDefId, _zoneKey, World.Id);
        }
    }

    /// <summary>
    /// Starts the copy's clock the first time a player is handed the copy after their load.
    /// </summary>
    /// <remarks>
    /// The clock is anchored to the arrival, not to the copy's creation or the load: a player who takes
    /// their time accepting the entry invite, or loading, must not burn the copy's ready and play budgets
    /// before they can see it. The first arrival is the copy's start; later arrivals join a clock already
    /// running.
    /// </remarks>
    private void AnchorCopyClock(Character character)
    {
        if (_copyStartUtc != null)
            return;

        _copyStartUtc = DateTime.UtcNow;
        Logger.Info($"[{World}] instance clock started on {character?.Name ?? "?"}'s arrival");
        // A copy whose ready budget is zero starts its script right here, without waiting for the tick.
        lock (_lock)
        {
            TickInstanceScript();
        }
    }

    /// <summary>
    /// Ends the copy's authored <c>tower_defs</c> run so its World-authored units do not outlive the copy.
    /// </summary>
    private void StopInstanceScript()
    {
        if (!_instanceScriptStarted || _zoneKey == 0 || World == null)
            return;

        _instanceScriptStarted = false;
        var stopped = WorldIntegration.EndInstanceTowerDef?.Invoke(
            _zoneKey, World.Id, _indunZone.Option.TowerDefId) == true;
        if (stopped)
            Logger.Info("[{0}] instance script: tower_def {1} stopped with the copy",
                World, _indunZone.Option.TowerDefId);
    }

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
        DespawnPlayerSummons(character);
        lock (_lock)
        {
            return World.RemoveObject(character);
        }
    }

    /// <summary>
    /// Player-summoned companions stay in the copy they were created in. Leaving without a
    /// dismiss leaves them standing at that summon point — retire them with the leaver.
    /// </summary>
    private void DespawnPlayerSummons(Character character)
    {
        if (World == null || character.Id == 0)
            return;

        List<Npc> victims;
        lock (_lock)
        {
            victims = World.GetAllNpcs()
                .Where(npc => SummonCompanionRules.IsPlayerSummonedCompanion(
                    npc.IsWorldAuthored, npc.OwnerId, character.Id))
                .ToList();
        }

        foreach (var npc in victims)
            WorldIntegration.DeleteNpcMirror(npc, notifyZone: true);
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

        // Stop the copy's authored tower_defs run before its host goes away, so the World-authored units
        // it left behind do not stick around after the copy is gone.
        StopInstanceScript();

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
        if (!PlaceCharacterAtInstanceSpawn(character))
        {
            character.SendErrorMessage(ErrorMessageType.NoServerInstanceResource);
            return;
        }

        character.Events.OnDungeonLeave += OnDungeonLeave;
        character.Events.OnDisconnect += OnDisconnect;
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
        if (!PlaceCharacterAtInstanceSpawn(character))
        {
            character.SendErrorMessage(ErrorMessageType.NoServerInstanceResource);
            return;
        }

        character.Events.OnTeamJoin += OnTeamJoin;
        character.Events.OnTeamKick += OnTeamLeave;
        character.Events.OnTeamLeave += OnTeamLeave;
        character.Events.OnDungeonLeave += OnDungeonLeave;
        character.Events.OnDisconnect += OnDisconnect;
    }

    /// <summary>
    /// Places the character at this copy's arrival point and sends the instance load. Returns false and
    /// reports when the copy has no arrival point to use.
    /// </summary>
    private bool PlaceCharacterAtInstanceSpawn(Character character)
    {
        var spawn = ResolveArrivalSpawn();
        if (spawn == null)
        {
            // Loud on purpose: without an arrival point the client is left at the zone origin, which is
            // under the map, and a silent Info hid that. Reaches the file log as a warning.
            Logger.Warn(
                "World #{0} has no arrival spawn for zone {1} (no spawn_point.g and no world_spawns entry)",
                World.Id, _zoneInstanceId.ZoneId);
            return false;
        }

        character.DisabledSetPosition = true;
        RememberMainWorldReturn(character);
        // The copy's zone is applied explicitly: a template spawn carries no zone of its own, and the
        // World routes the entering character by the zone on this transform.
        character.Transform.ApplyInstanceSpawnPosition(spawn, _zoneInstanceId.ZoneId, World.Id);
        // The instance's level authors its own fixed time-of-day. The client force-applies lighting
        // from the first hour it is handed after the load opens, so bind that hour here — the zone's
        // later report only eases and left the instance lit by the open-world clock.
        WorldIntegration.BindInstanceTimeOfDayBeforeLoad?.Invoke(character, World.Template.Name, _zoneInstanceId.ZoneId);
        character.SendPacket(
            new SCLoadInstancePacket(
                World.Id,
                _zoneInstanceId.ZoneId,
                spawn.X,
                spawn.Y,
                spawn.Z,
                spawn.Roll.DegToRad(),
                spawn.Pitch.DegToRad(),
                spawn.Yaw.DegToRad()));
        // The load packet alone carries the crossing. A unit-teleport sent with it is acted on in the world
        // the client is still in: it jumps there to the copy's coordinates, drops and re-requests that
        // world's cells, and only then loads the copy — the hitch on every entry and leave.
        // The copy's clock does not start here: the player is still on the loading screen. It starts with
        // the hand-over once the load is done (SendDungeonEntryHandshake).
        return true;
    }

    /// <summary>
    /// The spot a player arrives at inside this copy.
    /// </summary>
    /// <remarks>
    /// The zone's own <c>spawn_point.g</c> is the level pack's authored arrival point and wins. The world
    /// template's spawn is the older hand-kept table (<c>world_spawns.json</c>), which has no row for most
    /// instances; it is used only when the level file is absent. Neither carries the zone, so the caller
    /// applies <see cref="_zoneInstanceId"/>. Returns null when the copy has no arrival point at all.
    /// </remarks>
    private WorldSpawnPosition ResolveArrivalSpawn()
    {
        if (ZoneSpawnPointGCatalog.TryGetZoneSpawn(World.Template.Name, _zoneInstanceId.ZoneId, out var authored))
        {
            // The file is zone-local, the transform the World streams is continent: shift by the zone's
            // origin cell or the arrival lands in a cell this zone does not occupy (empty level).
            var origin = ZoneManager.Instance.GetZoneOriginCell(_zoneInstanceId.ZoneId);
            var world = ZoneSpawnPointFileRules.ToWorldCoordinates(
                origin.X, origin.Y, authored.X, authored.Y, authored.Z);
            return new WorldSpawnPosition
            {
                WorldId = World.Template.Id,
                ZoneId = _zoneInstanceId.ZoneId,
                X = world.X,
                Y = world.Y,
                Z = world.Z,
                Yaw = ZoneSpawnPointFileRules.YawDegreesFromZRot(authored.ZRotRadians)
            };
        }

        // Legacy: the hand-kept table's row for this zone, when the level pack has no spawn_point.g.
        foreach (var wz in World.Template.XmlWorldZones.Values)
        {
            if (wz.Id == _zoneInstanceId.ZoneId && wz.SpawnPosition != null && !IsUnplaced(wz.SpawnPosition))
                return wz.SpawnPosition;
        }

        return null;
    }

    /// <summary>True for a spawn row that was never filled in (the WorldSpawnLookups default).</summary>
    private static bool IsUnplaced(WorldSpawnPosition position) =>
        position.X == 0f && position.Y == 0f && position.Z == 0f;

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

        // The round's own limit travels separately (0x2DA): a round with an authored `indun_rounds.timer`
        // reports it so the client draws the countdown - zone group 130's "until dawn" is this timer.
        (uint limit, uint play, bool isTimeLimit) timer;
        lock (_lock)
        {
            timer = Rounds.RoundTimer(DateTime.UtcNow);
        }

        BroadcastToPlayers(new SCIndunUpdateRoundInfoPacket(
            IndunRoundRules.ToWireRound(round), timer.limit, timer.play, timer.isTimeLimit, nextRoundBoss));

        // The copy's players were told it was not playing yet (0x2D9 with playing=false), and that packet
        // is what the client opens its round HUD on, so the first round going live is told the same way.
        if (IndunRoundRules.OpensFirstRound(roundAlarmKindId, round))
        {
            BroadcastToPlayers(new SCIndunInitialRoundInfoPacket(
                IndunRoundRules.ToWireRound(round), IndunRoundRules.ToWireRound(Rounds.TotalRounds), playing));
        }
    }

    /// <summary>
    /// Hands the copy over to a client whose loading screen has closed, starts the copy's clock on the first
    /// arrival, and sends the copy's round and HUD readouts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A player who entered through a match, into a copy with a ready window, is told it has joined
    /// (<c>SCInstantGameJoinedPacket</c>): the client shows its standby banner and takes down the queue's
    /// standby button. The copy's tick then walks them through ready (the wait countdown) and start (the
    /// HUD) — see <see cref="AdvanceOpenings"/>. Each of those is accepted only from the stage before, and
    /// the client was put at the first one by accepting this copy's invite.
    /// </para>
    /// <para>
    /// Anyone else is handed the copy as already running with <c>SCInstantGameReentryPacket</c> (0x1E7),
    /// which takes the client straight to the started state. That packet also re-applies the instance UI
    /// permissions and replays the client's leaving-the-loading-screen handlers, so it is sent once, here,
    /// and never before the load as well.
    /// </para>
    /// <para>
    /// The client drops UI events raised while its loading screen is up, so this must not run from the
    /// load itself. The caller sends it once the client reports it has left the loading screen (its
    /// re-entry check).
    /// </para>
    /// </remarks>
    public void SendDungeonEntryHandshake(Character character)
    {
        if (character == null)
            return;

        AnchorCopyClock(character);

        bool opening;
        lock (_lock)
        {
            opening = _matchedEntries.Remove(character.Id) && IndunOpeningRules.UsesOpening(_indunZone.Option);
            if (opening)
                _openings[character.Id] = IndunOpeningStage.Joined;
        }

        if (opening)
        {
            character.SendPacket(new SCInstantGameJoinedPacket(_zoneInstanceId, InstantGameWireContract.NoBattleFieldType));
            Logger.Info($"[{World}] {character.Name} joined the copy; opening with standby");
        }
        else
        {
            character.SendPacket(new SCInstantGameReentryPacket(
                _zoneInstanceId,
                GetInstanceCatalogId,
                InstantGameWireContract.NoBattleFieldType,
                ClockOriginUnixSeconds()));
        }

        SendInitialRoundInfo(character);
        SendPlayingInfo(character);
    }

    /// <summary>
    /// The copy's clock origin in unix seconds; now while the copy has no clock yet. Seconds, because the
    /// client adds the option's ready / play / end seconds to it and counts down against its own seconds
    /// clock — milliseconds read as a countdown millions of seconds away.
    /// </summary>
    private long ClockOriginUnixSeconds() =>
        IndunInstancePhaseRules.TryGetClockOrigin(_copyStartUtc, out var origin)
            ? new DateTimeOffset(DateTime.SpecifyKind(origin, DateTimeKind.Utc)).ToUnixTimeSeconds()
            : Helpers.UnixTimeNow();

    /// <summary>
    /// Walks every matched player one step through the copy's opening: ready once they have seen the
    /// standby banner, start once the ready window is over. The ready packet carries the copy's clock
    /// origin, so the client counts the same ready window the copy runs; the start carries the moment the
    /// play phase began. Players who left the copy are dropped.
    /// </summary>
    private void AdvanceOpenings()
    {
        if (_openings.Count == 0 || World == null)
            return;
        if (!IndunInstancePhaseRules.TryGetClockOrigin(_copyStartUtc, out var clockOrigin))
            return;

        var option = _indunZone.Option;
        var phase = IndunInstancePhaseRules.PhaseAt(option, clockOrigin, DateTime.UtcNow);
        var origin = ClockOriginUnixSeconds();
        var playStart = origin + Math.Max(0, option.ReadySeconds);

        foreach (var (characterId, stage) in _openings.ToArray())
        {
            var character = WorldManager.Instance.GetCharacterById(characterId);
            if (character == null || !World.HasCharacter(characterId))
            {
                _openings.Remove(characterId);
                continue;
            }

            var step = IndunOpeningRules.Next(stage, phase);
            if (step.SendReady)
                character.SendPacket(new SCInstantGameReadyPacket(
                    _zoneInstanceId, InstantGameWireContract.NoBattleFieldType, origin));
            if (step.SendStart)
            {
                character.SendPacket(new SCInstantGameStartPacket(
                    _zoneInstanceId, playStart, (uint)IndunRoundRules.ToWireRound(Rounds.CurrentRound)));
                SendPlayingInfo(character);
            }

            if (step.Stage == IndunOpeningStage.Started)
                _openings.Remove(characterId);
            else
                _openings[characterId] = step.Stage;

            if (step.SendReady || step.SendStart)
                Logger.Info($"[{World}] {character.Name} opening → {step.Stage} (phase {phase})");
        }
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

        // A relog mid-round has to see the round's limit too, or the countdown disappears until the next alarm.
        if (playing)
        {
            var timer = Rounds.RoundTimer(DateTime.UtcNow);
            character.SendPacket(new SCIndunUpdateRoundInfoPacket(
                current, timer.LimitSeconds, timer.PlaySeconds, timer.IsTimeLimitRound, Rounds.NextRoundIsBoss));
        }
    }

    /// <summary>
    /// The units a copy's HUD readouts may be read from: the copy's own npcs plus every zone mirror that
    /// belongs to this copy.
    /// </summary>
    /// <remarks>
    /// A zone's units are mirrored into the world that owned the zone when they spawned — but a mirror can
    /// also be filed elsewhere, so the pool takes both the copy's own list and every mirror whose
    /// transform names this copy by instance id. Sibling copies share a zone key, so a zone match is
    /// not enough. Both sources are searched, keyed by object id so a unit present in both is one entry.
    /// </remarks>
    private List<Npc> FindReadoutUnits(List<Npc> copyNpcs)
    {
        var zoneId = _zoneInstanceId.ZoneId;
        var instanceId = World?.Id ?? 0u;
        var units = new List<Npc>(copyNpcs ?? []);
        var seen = new HashSet<uint>();
        foreach (var unit in units)
        {
            if (unit != null)
                seen.Add(unit.ObjId);
        }

        var mirrors = 0;
        foreach (var other in WorldManager.Instance.GetWorlds() ?? [])
        {
            foreach (var npc in other.GetAllNpcs())
            {
                if (npc is not { IsZoneMirror: true })
                    continue;

                if (!TowerDefCopyOwnershipRules.SameCopy(instanceId, npc.Transform?.InstanceId ?? 0))
                    continue;

                mirrors++;
                if (seen.Add(npc.ObjId))
                    units.Add(npc);
            }
        }

        // The pool is reported once per refresh: a readout with no unit behind it has to be readable as
        // "the unit is not here" rather than "the buff is missing".
        Logger.Debug(
            "HUD readout unit pool world {0} zone {1}: {2} unit(s), {3} mirror(s); templates [{4}]",
            instanceId, zoneId, units.Count, mirrors,
            string.Join(",", units.Select(u => u.TemplateId).Distinct().OrderBy(t => t).Take(40)));

        return units;
    }

    private SCIndunPlayingInfoBroadcastingPacket BuildPlayingInfoPacket() =>
        BuildPlayingInfoPacket(ReadPlayingInfoRows(freshUnits: true, log: true));

    private SCIndunPlayingInfoBroadcastingPacket BuildPlayingInfoPacket(List<IndunPlayingInfoNpc> rows)
    {
        // The handler reads the gain-rule ids twice — once as a 32-bit list and once as a 64-bit list — and
        // resolves both through the same lookup into its gainRuleInfo readout; missing the first list shifts
        // the second count and the client drops the whole packet. The copy's authored instance_gain_rules
        // ids are the one source, so they fill both from the table (empty for a zone group that authors none).
        var gainRuleIds = new List<uint>();
        var gainRules = new List<ulong>();
        foreach (var rule in IndunGameData.Instance.GetInstanceGainRules(GetZoneGroupId))
        {
            gainRuleIds.Add(rule.Id);
            gainRules.Add(rule.Id);
        }

        Logger.Debug(
            "HUD readout world {0} zoneGroup {1}: {2} npcInfo row(s) [{3}], gainRules={4}",
            World?.Id, GetZoneGroupId, rows.Count,
            string.Join(",", rows.Select(r => $"npc{r.NpcId}/buff{r.BuffId}={r.Value}/{r.Limit}")),
            gainRuleIds.Count);

        lock (_lock)
            _lastReadoutShape = IndunPlayingInfoRules.Shape(rows);

        return new SCIndunPlayingInfoBroadcastingPacket(_zoneInstanceId, rows, gainRuleIds, gainRules);
    }

    /// <summary>
    /// The units the copy's readouts are read from. A fresh search walks every world's mirrors, so the
    /// per-second change check reuses the last result and searches again only while a readout's unit is
    /// still missing, and then at most every <see cref="ReadoutUnitSearchInterval"/>.
    /// </summary>
    private List<Npc> GetReadoutUnits(bool fresh, IReadOnlyCollection<uint> templates)
    {
        var world = World;
        if (world == null)
            return [];

        var now = DateTime.UtcNow;
        var cached = _readoutUnits;
        var complete = cached != null && templates.All(t => cached.Exists(n => n != null && n.TemplateId == t));
        if (!fresh && (complete || (cached != null && now - _readoutUnitsSearchedAt < ReadoutUnitSearchInterval)))
            return cached;

        var units = FindReadoutUnits(world.GetAllNpcs());
        _readoutUnits = units;
        _readoutUnitsSearchedAt = now;
        return units;
    }

    private List<IndunPlayingInfoNpc> ReadPlayingInfoRows(bool freshUnits, bool log)
    {
        var world = World;
        var rows = new List<IndunPlayingInfoNpc>();
        var readouts = IndunGameData.Instance.GetIndunEvents(GetZoneGroupId)
            .OfType<IndunEventNpcInfoBroadcastings>()
            .Where(info => info.NpcId != 0 && info.BuffId != 0)
            .ToList();

        if (world != null && readouts.Count > 0)
        {
            var npcs = GetReadoutUnits(freshUnits, readouts.Select(r => r.NpcId).Distinct().ToList());
            foreach (var info in readouts)
            {
                foreach (var npc in npcs)
                {
                    if (npc == null || npc.TemplateId != info.NpcId)
                        continue;

                    var buff = npc.Buffs?.GetEffectFromBuffId(info.BuffId);
                    if (!IndunPlayingInfoRules.TryReadBuff(info.NpcInfoBroadcastingId, buff, out var value, out var limit))
                        continue;

                    // One row per authored readout: the copy's first unit of that template carries it, and
                    // a second unit of the same template must not send a duplicate row for the same npc.
                    rows.Add(new IndunPlayingInfoNpc(info.NpcId, info.BuffId, info.NpcInfoBroadcastingId, value, limit));
                    if (log)
                        Logger.Debug(
                            "HUD readout npc {0} buff {1} type {2} of world {3} read live from unit {4}: {5}/{6}",
                            info.NpcId, info.BuffId, info.NpcInfoBroadcastingId, World?.Id, npc.ObjId,
                            value, limit);
                    break;
                }
            }
        }

        // A readout whose unit the copy does not carry yet reads as not started: the script spawns the unit
        // and applies its buffs, and until then the HUD shows zeros.
        foreach (var info in readouts)
        {
            if (rows.Exists(row => row.NpcId == info.NpcId && row.BuffId == info.BuffId))
                continue;

            if (IndunPlayingInfoRules.TryReadAbsentBuff(info.NpcInfoBroadcastingId, out var value, out var limit))
            {
                rows.Add(new IndunPlayingInfoNpc(info.NpcId, info.BuffId, info.NpcInfoBroadcastingId, value, limit));
                continue;
            }

            if (log)
                Logger.Debug(
                    "HUD readout npc {0} buff {1} has unknown type {2} in world {3}; skipped",
                    info.NpcId, info.BuffId, info.NpcInfoBroadcastingId, World?.Id);
        }

        return rows;
    }

    /// <summary>
    /// Pushes the copy's HUD readout the moment one of its readings changes shape — a buff going up or
    /// down, a stack count moving, a running timer reaching its next second — instead of leaving the client
    /// on stale rows until the next coarse refresh.
    /// </summary>
    private void RefreshPlayingInfoOnChange()
    {
        if (!HasPlayers)
            return;

        var rows = ReadPlayingInfoRows(freshUnits: false, log: false);
        if (rows.Count == 0 || IndunPlayingInfoRules.Shape(rows) == _lastReadoutShape)
            return;

        BroadcastToPlayers(BuildPlayingInfoPacket(rows));
    }

    /// <summary>
    /// Refreshes the copy's HUD readouts for every player in it, unconditionally; per-second timer movement
    /// goes out through <see cref="RefreshPlayingInfoOnChange"/>.
    /// </summary>
    public void SendPlayingInfoBroadcast()
    {
        if (World == null)
            return;

        BroadcastToPlayers(BuildPlayingInfoPacket());
    }

    /// <summary>The copy's HUD readout to one player, sent with the other load-time info.</summary>
    public void SendPlayingInfo(Character character)
    {
        if (character == null || World == null)
            return;

        character.SendPacket(BuildPlayingInfoPacket());
    }

    /// <summary>
    /// Adds <paramref name="delta"/> to the copy's zone score of <paramref name="kindId"/> through the
    /// catalog rules (the kind's <c>max_score</c> clamp and its level resolution) and returns the applied
    /// change. The amount is always the caller's — nothing here invents one — and a kind the copy does not
    /// own is refused by the rules rather than created.
    /// </summary>
    public ZoneScoreApplication AddZoneScore(uint kindId, int delta) =>
        ZoneScores.Apply(kindId, delta);

    /// <summary>
    /// One applied zone-score change: the copy's players see it on the client's score list, and a move that
    /// changed the level runs the copy's authored <c>indun_event_zone_score_level_changeds</c> chains.
    /// </summary>
    private void OnZoneScoreChanged(ZoneScoreApplication application)
    {
        new FactionScoringNotifier(BroadcastToPlayers).PublishZoneScoreChange(application);

        if (application.LevelChanged)
            IndunManager.Instance.DoZoneScoreLevelChangedEvents(World, application);
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
            // A scripted copy starts its authored tower_defs run once its ready ("wait time") window has
            // elapsed — checked here because this is the copy's own per-second tick.
            TickInstanceScript();
            AdvanceOpenings();
            RefreshPlayingInfoOnChange();

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
