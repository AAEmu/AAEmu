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
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly List<uint> _players;
    private readonly World.World _world;
    private readonly ZoneInstanceId _zoneInstanceId;
    public readonly IndunZone _indunZone;
    // unused private List<Character> _teleportList;
    private readonly ConcurrentDictionary<uint, DateTime> _leaveRequests;
    private Character _characterOwner;
    private Team.Team _teamOwner;
    private bool _isTeamOwned;
    private readonly Dictionary<uint, bool> _rooms;
    //private static Dictionary<uint, Dictionary<uint, int>> _attempts; // <ownerId, <zoneGroupId, attempts>> - использовано попыток прохождения данжона
    //private const int FreeAttempts = 3;  // свободных попыток
    //private const int ExtraAttempts = 2; // дополнительных попыток
    //public bool IsWaitingDungeonAccessAttemptsCleared { get; set; }

    private readonly object _lock = new();

    public bool IsTeamOwned { get => _isTeamOwned; }
    public Character GetCharacterOwner { get => _characterOwner; }
    public Team.Team GetTeamOwner { get => _teamOwner; }
    public uint GetZoneGroupId { get => _indunZone.ZoneGroupId; }
    public bool IsSystem { get => !_isTeamOwned && _characterOwner == null; }

    public Dungeon(IndunZone indunZone, Character character, Team.Team team = null)
    {
        _indunZone = indunZone;
        _players = [];
        _leaveRequests = new ConcurrentDictionary<uint, DateTime>();
        _rooms = [];
        _characterOwner = character;

        if (team == null)
        {
            _isTeamOwned = false;
        }
        else
        {
            _characterOwner = null;
            _teamOwner = team;
            _isTeamOwned = true;
        }

        if (_teamOwner != null)
        {
            TickManager.Instance.OnTick.Subscribe(LeaveDungeonTick, TimeSpan.FromSeconds(1), true);
        }

        TickManager.Instance.OnTick.Subscribe(AreaClearTick, TimeSpan.FromSeconds(1), true);
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

        var world = WorldManager.Instance.GetWorldByZone(zoneKeys[0]);
        //if (_indunZone.ZoneGroupId is 49 or 70 or 71 or 72)
        Logger.Info($"[Dungeon] Create dungeon...");
        Logger.Info($"[Dungeon] делаем копию инстанса...");
        _world = WorldManager.Instance.CreateWorld(world); // делаем копию инстанса
        Logger.Info($"[Dungeon] сделали копию инстанса...");

        _zoneInstanceId = new ZoneInstanceId(zoneKeys[0], _world.Id);
        // выводится окошко о том, что создается данжон
        character.SendPacket(new SCProcessingInstancePacket((int)_zoneInstanceId.ZoneId));

        // let's spawn Npc, Doodad, Slave, Gimmick
        Logger.Info($"[Dungeon] spawning Npc, Doodad, Slave, Gimmick...");
        _ = SpawnManager.Instance.SpawnAll(_world.Id, _world.TemplateId);
        Logger.Info($"[Dungeon] spawned Npc, Doodad, Slave, Gimmick...");

        RegisterIndunEvents();
    }

    /// <summary>
    /// For system dungeons like the mirage and the library
    /// для системных данжей, таких как мираж и библиотека
    /// </summary>
    /// <param name="indunZone"></param>
    /// <param name="character"></param>
    public Dungeon(IndunZone indunZone, Character character)
    {
        _indunZone = indunZone;
        _players = [];
        _leaveRequests = new ConcurrentDictionary<uint, DateTime>();
        _rooms = [];
        //_characterOwner = character;

        _isTeamOwned = false;
        _characterOwner = null;

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
        var world = WorldManager.Instance.GetWorldByZone(zoneKeys[0]);

        Logger.Info($"[Dungeon] Create system dungeon...");
        // для zone_key: 260=arche_mall, 296=instance_library_1, 297=instance_library_2, 298=instance_library_3
        // или
        // для group_id: 49=arche_mall, 70=instance_library_1, 71=instance_library_2, 72=instance_library_3
        Logger.Info($"[Dungeon] не делаем копию инстанса...");
        _world = world; // don't make a copy of the instance / не делаем копию инстанса
        _zoneInstanceId = new ZoneInstanceId(zoneKeys[0], _world.Id);
        // a window will pop up indicating that a dungeon is being created. / выводится окошко о том, что создается данжон
        character.SendPacket(new SCProcessingInstancePacket((int)_zoneInstanceId.ZoneId));

        RegisterIndunEvents();
    }

    /// <summary>
    /// Returns true if the dungeon is full capacity, false if not.
    /// </summary>
    public bool IsFull => _players.Count == _indunZone.MaxPlayers;

    /// <summary>
    /// Returns true if the dungeon has players inside, false if not.
    /// </summary>
    private bool HasPlayers => _players.Count > 0;

    /// <summary>
    /// Add player to Dungeon
    /// </summary>
    /// <param name="character"></param>
    public void AddPlayer(Character character)
    {
        Logger.Info($"[Dungeon] Adding player {character.Name} to dungeon {_zoneInstanceId.InstanceId}, {_zoneInstanceId.ZoneId}");

        lock (_lock)
        {
            if (!_players.Contains(character.Id))
            {
                _players.Add(character.Id);
            }
            else
            {
                Logger.Info($"[Dungeon] Player {character.Name} already exists in dungeon {_zoneInstanceId.InstanceId}, {_zoneInstanceId.ZoneId}. Most likely an error in logic?");
            }
        }

        if (IsSystem)
        {
            MoveCharacterToSysWorld(character);
        }
        else
        {
            MoveCharacterToWorld(character);
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
            return _players.Remove(character.Id);
        }
    }

    /// <summary>
    /// Destroys dungeon instance for teams
    /// </summary>
    private async Task DestroyTeamDungeon()
    {
        await Task.Delay(5000);

        Logger.Info($"[Dungeon] instanceId={_zoneInstanceId.InstanceId}, zoneId={_zoneInstanceId.ZoneId}: Destroying team dungeon...");

        IndunManager.Instance.RemoveDungeon(_teamOwner);
        if (_world == null) { return; }

        UnregisterIndunEvents();

        var npcList = new List<Npc>();
        foreach (var region in _world.Regions)
        {
            region?.GetList(npcList, 0);
        }

        foreach (var npc in npcList)
        {
            if (npc == null) { continue; }

            npc.UnregisterNpcEvents();
            npc.Delete();
            ObjectIdManager.Instance.ReleaseId(npc.ObjId);
        }

        WorldManager.Instance.RemoveWorld(_world.Id);
        WorldIdManager.Instance.ReleaseId(_world.Id);
    }

    /// <summary>
    ///  Destroys dungeon instance for solo players
    /// </summary>
    public bool DestroySoloDungeon(Character character, Dungeon soloDungeon)
    {
        if (character == null) { return false; }

        Logger.Info($"[Dungeon] instanceId={_zoneInstanceId.InstanceId}, zoneId={_zoneInstanceId.ZoneId}: Destroying solo dungeon...");

        TickManager.Instance.OnTick.UnSubscribe(AreaClearTick);

        _ = RemovePlayer(character);

        if (!soloDungeon.IsOwner(character) || soloDungeon.HasPlayers) { return false; }
        if (_world == null) { return true; }

        UnregisterIndunEvents();

        var npcList = new List<Npc>();

        foreach (var region in _world.Regions)
        {
            region?.GetList(npcList, 0);
        }

        foreach (var npc in npcList)
        {
            if (npc == null) { continue; }

            npc.UnregisterNpcEvents();
            npc.Delete();
            ObjectIdManager.Instance.ReleaseId(npc.ObjId);
        }

        WorldManager.Instance.RemoveWorld(_world.Id);
        WorldIdManager.Instance.ReleaseId(_world.Id);

        return true;

    }

    /// <summary>
    /// Moves character to instanced dungeon world
    /// </summary>
    /// <param name="character"></param>
    private void MoveCharacterToSysWorld(Character character)
    {
        // we take the coordinates of the zone
        foreach (var wz in _world.XmlWorldZones.Values)
        {
            if (wz.Id == _zoneInstanceId.ZoneId)
            {
                _world.SpawnPosition = wz.SpawnPosition;
                break;
            }
        }
        if (_world.SpawnPosition != null)
        {
            character.DisabledSetPosition = true;
            //character.MainWorldPosition = character.Transform.CloneDetached(character); // сохраним координаты для возврата в основной мир
            character.SendPacket(
                new SCLoadInstancePacket(
                    _world.Id,
                    _zoneInstanceId.ZoneId,
                    _world.SpawnPosition.X,
                    _world.SpawnPosition.Y,
                    _world.SpawnPosition.Z,
                _world.SpawnPosition.Roll.DegToRad(),
                _world.SpawnPosition.Pitch.DegToRad(),
                _world.SpawnPosition.Yaw.DegToRad()));
            character.Transform.ApplyWorldSpawnPosition(_world.SpawnPosition, _world.Id);
            character.InstanceId = _world.Id;

            character.Events.OnDungeonLeave += OnDungeonLeave;
            character.Events.OnDisconnect += OnDisconnect;
        }
        else
        {
            Logger.Info($"World #{_world.Id}, not have default spawn position.");
            character.SendErrorMessage(ErrorMessageType.NoServerInstanceResource);
        }
    }
    /// <summary>
    /// Moves character to instanced dungeon world
    /// </summary>
    /// <param name="character"></param>
    private void MoveCharacterToWorld(Character character)
    {
        // we take the coordinates of the zone
        foreach (var wz in _world.XmlWorldZones.Values)
        {
            if (wz.Id == _zoneInstanceId.ZoneId)
            {
                _world.SpawnPosition = wz.SpawnPosition;
                break;
            }
        }
        if (_world.SpawnPosition != null)
        {
            character.DisabledSetPosition = true;
            //character.MainWorldPosition = character.Transform.CloneDetached(character); // сохраним координаты для возврата в основной мир
            character.SendPacket(
                new SCLoadInstancePacket(
                    _world.Id,
                    _zoneInstanceId.ZoneId,
                    _world.SpawnPosition.X,
                    _world.SpawnPosition.Y,
                    _world.SpawnPosition.Z,
                _world.SpawnPosition.Roll.DegToRad(),
                _world.SpawnPosition.Pitch.DegToRad(),
                _world.SpawnPosition.Yaw.DegToRad()));
            character.Transform.ApplyWorldSpawnPosition(_world.SpawnPosition, _world.Id);
            character.InstanceId = _world.Id;

            character.Events.OnTeamJoin += OnTeamJoin;
            character.Events.OnTeamKick += OnTeamLeave;
            character.Events.OnTeamLeave += OnTeamLeave;
            character.Events.OnDungeonLeave += OnDungeonLeave;
            character.Events.OnDisconnect += OnDisconnect;
        }
        else
        {
            Logger.Info($"World #{_world.Id}, not have default spawn position.");
            character.SendErrorMessage(ErrorMessageType.NoServerInstanceResource);
        }
    }

    /// <summary>
    /// Moves player out of the instanced dungeon world.
    /// </summary>
    /// <param name="character"></param>
    private void LeaveInstance(Character character)
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
            Logger.Info($"World #.{_world.Id}, not have Main World spawn position.");
            return;
        }

        character.DisabledSetPosition = true;
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
        character.InstanceId = character.MainWorldPosition.WorldId;
        character.Transform = character.MainWorldPosition.Clone();
    }
    internal void LeaveSysInstance(Character character)
    {
        character.Events.OnDungeonLeave -= OnDungeonLeave;
        character.Events.OnDisconnect -= OnDisconnect;

        _leaveRequests.TryRemove(character.Id, out _);
        _ = RemovePlayer(character);

        if (character.MainWorldPosition == null)
        {
            Logger.Info($"World #.{_world.Id}, not have Main World spawn position.");
            return;
        }

        character.DisabledSetPosition = true;
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
        character.InstanceId = character.MainWorldPosition.WorldId;
        character.Transform = character.MainWorldPosition.Clone();
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
            if (!IndunManager.Instance.SoloToParty(character, team, this)) { return; }
            _teamOwner = team;
            TickManager.Instance.OnTick.Subscribe(LeaveDungeonTick, TimeSpan.FromSeconds(1), true);
            _isTeamOwned = true;
            _characterOwner = null;
            Logger.Info($"[Dungeon] instanceId: {_zoneInstanceId.InstanceId}, zoneId: {_zoneInstanceId.ZoneId}. Converting solo instance into a party instance.");
            return;
        }

        if (PlayerInSameTeam(character) && !_players.Contains(character.Id))
        {
            _players.Add(character.Id);
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
        if (_players.Contains(character.Id) && character.InstanceId == _zoneInstanceId.InstanceId)
        {
            _leaveRequests.TryAdd(character.Id, DateTime.UtcNow.AddSeconds(30));
        }
    }

    private void OnDungeonLeave(object sender, OnDungeonLeaveArgs args)
    {
        var character = args.Player;
        if (character == null) { return; }

        Logger.Info($"Player={character.Name} has exit from dungeon={_world.Id}!");

        if (IsSystem)
        {
            LeaveSysInstance(character);
            return;
        }

        if (character.InParty)
        {
            // выход из данжона (без его удаления)
            LeaveInstance(character);
            return;
        }

        if (!_players.Contains(character.Id) || character.InstanceId != _zoneInstanceId.InstanceId) { return; }
        // выход из данжона (с его удалением)
        //IndunManager.Instance.RequestDeletion(character, this);
        LeaveInstance(character);
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
            IndunManager.Instance.RequestDeletion(args.Player, this);
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
    private bool PlayerInSameTeam(Character player)
    {
        if (_isTeamOwned == false) { return false; }

        return _teamOwner.Id == TeamManager.Instance.GetTeamByObjId(player.ObjId).Id;
    }

    private bool IsOwner(Character character)
    {
        return _isTeamOwned == false && _characterOwner?.Id == character?.Id;
    }

    public bool IsPlayerInDungeon(uint characterId)
    {
        return _players.Contains(characterId);
    }

    /// <summary>
    /// Удаляем данжон, когда все игроки тимы вышли оффлайн
    /// </summary>
    /// <param name="delta"></param>
    private void LeaveDungeonTick(TimeSpan delta)
    {
        if (_teamOwner != null)
        {
            if (_teamOwner.MembersOnlineCount() == 0 && _leaveRequests.IsEmpty)
            {
                TickManager.Instance.OnTick.UnSubscribe(LeaveDungeonTick);
                TickManager.Instance.OnTick.UnSubscribe(AreaClearTick);
                _ = DestroyTeamDungeon();
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

        foreach (var leaveRequest in _leaveRequests)
        {
            if (DateTime.UtcNow <= leaveRequest.Value) { continue; }

            Logger.Info($"[Dungeon] instanceId={_zoneInstanceId.InstanceId}, zoneId={_zoneInstanceId.ZoneId}: Removing qualifying players from instance.");
            var character = WorldManager.Instance.GetCharacterById(leaveRequest.Key);
            if (character == null)
            {
                Logger.Info($"[Dungeon] instanceId={_zoneInstanceId.InstanceId}, zoneId={_zoneInstanceId.ZoneId}: Player Id not found. Remove from processing..");
                _leaveRequests.TryRemove(leaveRequest);
                return;
            }
            if (character.InParty)
            {
                if (PlayerInSameTeam(character))
                {
                    Logger.Info($"[Dungeon] instanceId={_zoneInstanceId.InstanceId}, zoneId={_zoneInstanceId.ZoneId}: player={character.Name} rejoined party, aborting.");
                    _leaveRequests.TryRemove(leaveRequest);
                    return;
                }
            }

            LeaveInstance(character);
        }
    }

    private void RegisterIndunEvents()
    {
        Logger.Info($"Registering Indun Events...");
        foreach (var ev in IndunGameData.Instance.GetIndunEvents(_indunZone.ZoneGroupId))
        {
            ev?.Subscribe(_world);
        }
    }

    private void UnregisterIndunEvents()
    {
        Logger.Info($"Unregistering Indun Events...");
        foreach (var ev in IndunGameData.Instance.GetIndunEvents(_indunZone.ZoneGroupId))
        {
            ev?.UnSubscribe(_world);
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
        return _world.Id;
    }

    public uint GetDungeonTemplateId()
    {
        return _world.TemplateId;
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
                var doodad = room.GetRoomDoodad(_world.Id);

                if (doodad == null) { continue; }

                var radiusCount = WorldManager.GetAround<Character>(doodad, indunRoom.Radius)
                    .Where(o => o.GetDistanceTo(doodad) <= indunRoom.Radius).ToList().Count;

                Logger.Info($"Character:{radiusCount} in room:{room.RoomId}");

                if (radiusCount == 0 && room.GetRoomPlayerCount(_world.Id) != 0)
                {
                    IndunManager.DoIndunActions(ev.StartActionId, _world);
                }

                room.SetRoomPlayerCount(_world.Id, (uint)radiusCount);
            }
        }
    }

    public int GetPlayerCount()
    {
        return _players.Count;
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
