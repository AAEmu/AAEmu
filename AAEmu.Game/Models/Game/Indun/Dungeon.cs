using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun.Events;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Indun;

public class Dungeon
{
    // ReSharper disable once InconsistentNaming
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// List of players who have been granted access to this dungeon (and did not reset it yet)
    /// </summary>
    public HashSet<uint> PlayersWithAccess { get; init; } = [];

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
    //private static Dictionary<uint, Dictionary<uint, int>> _attempts; // <ownerId, <zoneGroupId, attempts>> - dungeon attempts used
    //private const int FreeAttempts = 3;  // free attempts
    //private const int ExtraAttempts = 2; // additional attempts
    //public bool IsWaitingDungeonAccessAttemptsCleared { get; set; }

    // ReSharper disable once ChangeFieldTypeToSystemThreadingLock
    private readonly object _lock = new();

    public bool IsTeamOwned { get => _isTeamOwned; }
    public Character GetCharacterOwner { get => _characterOwner; }
    public Team.Team GetOwnerTeam { get => _ownerTeam; }
    public uint GetZoneGroupId { get => _indunZone.ZoneGroupId; }
    public bool IsSystem { get; set; }
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
    public Dungeon(IndunZone indunZone, Character character, uint channelId, Team.Team team, bool overrideInstanceId = false, uint fixedInstanceId = 0)
    {
        _indunZone = indunZone;
        _leaveRequests = new ConcurrentDictionary<uint, DateTime>();
        _rooms = [];

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

        Logger.Info($"[Dungeon] Create system dungeon...");
        // для zone_key: 260=arche_mall, 296=instance_library_1, 297=instance_library_2, 298=instance_library_3
        // или
        // для group_id: 49=arche_mall, 70=instance_library_1, 71=instance_library_2, 72=instance_library_3
        Logger.Info($"[Dungeon] don't make a copy of the instance ...");
        World = WorldManager.Instance.CreateWorldInstance(worldTemplate, channelId, overrideInstanceId, fixedInstanceId, character);
        World.DungeonInstance = this;
        // If started by a character, enter them into queue
        if (character != null)
        {
            PlayersWithAccess.Add(character.Id);
            EnterRequests.Add(character);
        }
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
        _zoneInstanceId = new ZoneInstanceId(zoneKeys.First(), World.Id);

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

    /// <summary>
    /// Adds a player to the queue while the dungeon is still loading
    /// </summary>
    /// <param name="character"></param>
    public bool QueuePlayer(Character character)
    {
        if (EnterRequests.Contains(character))
            return false;
        
        if (!IndunManager.Instance.CheckingAttempt(this))
        {
            Logger.Info($"[{World}] Player {character.Name} did too many dungeon attempts.");
            character.SendErrorMessage(ErrorMessageType.InstanceVisitLimit);
            return false;
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
    /// Add player to Dungeon
    /// </summary>
    /// <param name="character"></param>
    public void AddPlayer(Character character)
    {
        Logger.Info($"[Dungeon] Adding player {character.Name} to dungeon {_zoneInstanceId.InstanceId}, {_zoneInstanceId.ZoneId}");

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

        // Despawn everything
        World.SpawnManager.DeSpawnAll();

        WorldManager.Instance.RemoveWorld(World.Id);
        WorldIdManager.Instance.ReleaseId(World.Id);
        
        World = null;
    }

    /// <summary>
    /// Destroys dungeon instance
    /// </summary>
    public bool DestroyDungeon()
    {
        Logger.Info($"[Dungeon] instanceId={_zoneInstanceId.InstanceId}, zoneId={_zoneInstanceId.ZoneId}: Destroying dungeon...");

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

        World.CleanupInstance();

        WorldManager.Instance.RemoveWorld(World.Id);
        WorldIdManager.Instance.ReleaseId(World.Id);

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
            //character.MainWorldPosition = character.Transform.CloneDetached(character); // сохраним координаты для возврата в основной мир
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
            //character.MainWorldPosition = character.Transform.CloneDetached(character); // сохраним координаты для возврата в основной мир
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

        if (character.MainWorldPosition == null)
        {
            Logger.Info($"World #.{World.Id}, does not have Main World spawn position.");
            return;
        }

        character.DisabledSetPosition = true;
        character.Transform = character.MainWorldPosition.Clone();
        character.Transform.InstanceId = WorldManager.DefaultInstanceId;
        character.SendPacket(
            new SCLoadInstancePacket(
                character.MainWorldPosition.WorldId,
                character.MainWorldPosition.ZoneId,
                character.MainWorldPosition.World.Position.X,
                character.MainWorldPosition.World.Position.Y,
                character.MainWorldPosition.World.Position.Z,
                character.MainWorldPosition.World.Rotation.X.DegToRad(),
                character.MainWorldPosition.World.Rotation.Y.DegToRad(),
                character.MainWorldPosition.World.Rotation.Z.DegToRad()
            )
        );
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

        if (character.MainWorldPosition == null)
        {
            Logger.Info($"World #.{World.Id}, did not have a return point set in main world for {character.Name} ({character.Id}) !");
            return;
        }

        character.DisabledSetPosition = true;
        character.Transform = character.MainWorldPosition.Clone();
        character.Transform.InstanceId = WorldManager.DefaultInstanceId;
        character.SendPacket(
            new SCLoadInstancePacket(
                character.MainWorldPosition.WorldId,
                character.MainWorldPosition.ZoneId,
                character.MainWorldPosition.World.Position.X,
                character.MainWorldPosition.World.Position.Y,
                character.MainWorldPosition.World.Position.Z,
                character.MainWorldPosition.World.Rotation.X.DegToRad(),
                character.MainWorldPosition.World.Rotation.Y.DegToRad(),
                character.MainWorldPosition.World.Rotation.Z.DegToRad()
            )
        );
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

        Logger.Info($"Player {character.Name} ({character.Id}) has exited from dungeon {World}!");

        if (character.ParentWorld.DungeonInstance != null)
        {
            if (character.ParentWorld.DungeonInstance.IsSystem)
            {
                LeaveSystemInstance(character);
            }
            else
            {
                LeaveDungeonInstance(character);
            }
        }
    }

    private void OnDisconnect(object sender, OnDisconnectArgs args)
    {
        Logger.Info($"[Dungeon] instanceId={_zoneInstanceId.InstanceId}, zoneId={_zoneInstanceId.ZoneId} player={args.Player.Name} disconnected!");

        if (IsSystem)
        {
            _ = RemovePlayer(args.Player);
            args.Player.Events.OnDungeonLeave -= OnDungeonLeave;
            args.Player.Events.OnDisconnect -= OnDisconnect;
            return;
        }

        if (!args.Player.InParty)
        {
            var zone = ZoneManager.Instance.GetZoneByKey(World.Template.ZoneKeys.First());
            IndunManager.Instance.RequestDeletion(args.Player, zone);
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
    /// Deletes a dungeon when all players in the team are offline
    /// </summary>
    /// <param name="delta"></param>
    private void LeaveDungeonTick(TimeSpan delta)
    {
        if (_ownerTeam != null)
        {
            if (_ownerTeam.MembersOnlineCount() == 0 && _leaveRequests.IsEmpty)
            {
                _ = DestroyDungeon();
            }
            else if (_leaveRequests.IsEmpty)
            {
                return;
            }
        }
        else if (_leaveRequests.IsEmpty)
        {
            return;
        }

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

                if (IsRoomCleared(room.RoomId)) { return; }

                var indunRoom = IndunGameData.Instance.GetRoom(room.RoomId);
                var doodad = room.GetRoomDoodad(World.Id);

                if (doodad == null) { continue; }

                var radiusCount = WorldManager.GetAround<Character>(doodad, indunRoom.Radius)
                    .Where(o => o.GetDistanceTo(doodad) <= indunRoom.Radius).ToList().Count;

                Logger.Info($"Character:{radiusCount} in room:{room.RoomId}");

                if (radiusCount == 0 && room.GetRoomPlayerCount(World.Id) != 0)
                {
                    IndunManager.DoIndunActions(ev.StartActionId, World);
                }

                room.SetRoomPlayerCount(World.Id, (uint)radiusCount);
            }
        }
    }

    public void WaitingDungeonAccessAttemptsCleared(TimeSpan delta)
    {
        if (!IndunManager.Instance.GetWaitingDungeonAccess(this))
        {
            IndunManager.Instance.SetWaitingDungeonAccess(this, true);
            return;
        }
        TickManager.Instance.OnTick.UnSubscribe(WaitingDungeonAccessAttemptsCleared);
        IndunManager.Instance.ClearAttemts(this);
    }
}
