using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Team;

using NLog;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Core.Managers;

public class IndunManager : Singleton<IndunManager>
{
    private static Logger Logger = LogManager.GetCurrentClassLogger();

    private Dictionary<Team, List<Dungeon>> _teamDungeons;
    private Dictionary<uint, Dungeon> _soloDungeons;
    private Dictionary<uint, Dungeon> _sysDungeons;
    private Dictionary<uint, Dictionary<uint, int>> _attempts; // <ownerId, <zoneGroupId, attempts>> - использовано попыток прохождения данжона
    private const int FreeAttempts = 3;  // свободных попыток
    // Unused private const int ExtraAttempts = 2; // дополнительных попыток
    private Dictionary<uint, Dictionary<uint, bool>> _waitingDungeonAccessAttemptsCleared; // <ownerId, <zoneGroupId, waiting>>, откат 4 часа, + еще 4 часа, если израсходовали дополнительные попытки

    private object _lock = new();

    public void Initialize()
    {
        _teamDungeons = [];
        _soloDungeons = [];
        _sysDungeons = [];
        TickManager.Instance.OnTick.Subscribe(IndunInfoTick, TimeSpan.FromSeconds(10), true);
        _attempts ??= [];
        _waitingDungeonAccessAttemptsCleared ??= [];
    }

    private void IndunInfoTick(TimeSpan delta)
    {
        if (_teamDungeons is { Count: > 0 })
        {
            Logger.Info($"Team dungeons: {_teamDungeons.Count}");
        }
        if (_soloDungeons is { Count: > 0 })
        {
            Logger.Info($"Solo dungeons: {_soloDungeons.Count}");
        }
        if (_sysDungeons is { Count: > 0 })
        {
            Logger.Info($"Sys dungeons: {_sysDungeons.Count}");
        }
        foreach (var td in _teamDungeons)
        {
            Logger.Info($"Team dungeon: team Id={td.Key.Id}, member counts={td.Key.MembersCount()}:");
            foreach (var dungeon in td.Value)
            {
                Logger.Info($"- {dungeon.GetPlayerCount()} players in dungeon Id={dungeon.GetDungeonWorldId()}");
            }
        }
        foreach (var sd in _soloDungeons)
        {
            Logger.Info($"Solo dungeon for char Id={sd.Key}: {sd.Value.GetPlayerCount()} player in dungeon Id={sd.Value.GetDungeonWorldId()}");
        }
        foreach (var sd in _sysDungeons)
        {
            Logger.Info($"Sys dungeon for zone Id={sd.Key}: {sd.Value.GetPlayerCount()} player in dungeon Id={sd.Value.GetDungeonWorldId()}");
        }

        InfoAttempt();
    }

    /// <summary>
    /// Requests an instance for the character's team or for the player.
    /// </summary>
    /// <param name="character"></param>
    /// <param name="zoneId"></param>
    /// <returns></returns>
    public bool RequestSysInstance(Character character, uint zoneId)
    {
        // TODO ZoneId=183 - Arche mall
        if (character == null)
        {
            Logger.Info("[IndunManager] Player offline.");
            return false;
        }

        var dungeon = CreateSysInstance(character, zoneId);
        if (dungeon == null)
        {
            return false;
        }

        dungeon.AddPlayer(character);

        return true;
    }

    /// <summary>
    /// Requests an instance for the character's team or for the player.
    /// </summary>
    /// <param name="character"></param>
    /// <param name="zoneId"></param>
    /// <returns></returns>
    public bool RequestInstance(Character character, uint zoneId)
    {
        if (character == null)
        {
            Logger.Info("[IndunManager] Player offline.");
            return false;
        }

        if (character.InParty)
        {
            var team = TeamManager.Instance.GetTeamByObjId(character.ObjId);
            if (team == null)
            {
                Logger.Info("[IndunManager] There is no such member on the team.");
                character.SendErrorMessage(ErrorMessageType.TeamNoSuchMember);
                return false;
            }

            _teamDungeons.TryGetValue(team, out var teamDungeons);
            if (teamDungeons != null)
            {
                foreach (var instance in teamDungeons)
                {
                    if (instance?._indunZone?.ZoneGroupId != ZoneManager.Instance.GetZoneById(zoneId)?.GroupId)
                    {
                        continue;
                    }

                    // так как уже израсходовали предмет на создание данжона, то более не проверяем
                    //if (instance?._indunZone is { ItemRequired: > 0 } && !PortalManager.CheckItemAndRemove(character, instance._indunZone.ItemRequired, 1))
                    //{
                    //    Logger.Info(
                    //        "[IndunManager] There is not the right item in the Inventory to visit the area.");
                    //    character.SendErrorMessage(ErrorMessageType.EnterInstReqItem);
                    //    return false;
                    //}

                    if (!(character.Level >= instance?._indunZone?.LevelMin && character.Level <= instance._indunZone?.LevelMax))
                    {
                        Logger.Info("[IndunManager] Not the right level of character to visit the area.");
                        character.SendErrorMessage(ErrorMessageType.InstanceLevel);
                        return false;
                    }

                    if (instance.IsFull)
                    {
                        Logger.Info("[IndunManager] There is no place for you in this area.");
                        character.SendErrorMessage(ErrorMessageType.InstanceQuota);
                        return false;
                    }

                    instance.AddPlayer(character);
                    return true;
                }
            }

            Logger.Info("[IndunManager] New team requesting instance.");
            return CreateTeamInstance(team, character, zoneId);

            //Logger.Info("[IndunManager] No instance in server resources.");
            //character.SendErrorMessage(ErrorMessageType.NoServerInstanceResource);
            //return false;
        }

        _soloDungeons.TryGetValue(character.Id, out var dungeon);
        if (dungeon == null)
        {
            return CreateSoloInstance(character, zoneId);
        }

        if (dungeon._indunZone?.ZoneGroupId != ZoneManager.Instance.GetZoneById(zoneId)?.GroupId)
        {
            Logger.Info("[IndunManager] Solo dungeon request on different area. Deleting saved solo dungeon.");
            character.SendErrorMessage(ErrorMessageType.ProhibitedInInstance);
            RequestDeletion(character, dungeon);
            return false;
        }

        if (dungeon._indunZone?.PartyRequired == true)
        {
            Logger.Info("[IndunManager] It is required to be in the party to visit this area. Deleting saved solo dungeon.");
            character.SendErrorMessage(ErrorMessageType.NeedParty);
            character.SendErrorMessage(ErrorMessageType.CannotFollowNonParty);
            character.SendErrorMessage(ErrorMessageType.InstanceLeaveParty);
            RequestDeletion(character, dungeon);
            return false;
        }

        // так как уже израсходовали предмет на создание данжона, то более не проверяем
        //if (dungeon._indunZone is { ItemRequired: > 0 } && !PortalManager.CheckItemAndRemove(character, dungeon._indunZone.ItemRequired, 1))
        //{
        //    Logger.Info("[IndunManager] There is not the right item in the Inventory to visit the area. Deleting saved solo dungeon.");
        //    character.SendErrorMessage(ErrorMessageType.EnterInstReqItem);
        //    RequestDeletion(character, dungeon);
        //    return false;
        //}

        if (!(character.Level >= dungeon._indunZone?.LevelMin && character.Level <= dungeon._indunZone?.LevelMax))
        {
            Logger.Info("[IndunManager] Not the right level of character to visit the area. Deleting saved solo dungeon.");
            character.SendErrorMessage(ErrorMessageType.InstanceLevel);
            RequestDeletion(character, dungeon);
            return false;
        }

        if (dungeon.IsFull)
        {
            Logger.Info("[IndunManager] There is no place for you in this area. Deleting saved solo dungeon.");
            character.SendErrorMessage(ErrorMessageType.InstanceQuota);
            RequestDeletion(character, dungeon);
            return false;
        }

        Logger.Info("[IndunManager] Solo dungeon matches area.");
        //character.SendErrorMessage(ErrorMessageType.InstanceInMsgStart);
        dungeon.AddPlayer(character);

        return true;
    }

    private bool CreateTeamInstance(Team team, Character character, uint zoneId)
    {
        Logger.Info($"[IndunManager] Requesting party instance, teamId: {team.Id}");
        Logger.Info($"[IndunManager] Total dungeons created... Party: {_teamDungeons.Count}... Solo: {_soloDungeons.Count}...");

        var zoneGroupId = IndunGameData.Instance.GetDungeonZone(ZoneManager.Instance.GetZoneById(zoneId).GroupId).ZoneGroupId;
        if (GetWaitingDungeonAccess(character.Id, zoneGroupId))
        {
            Logger.Info("[IndunManager] The team has exhausted its attendance limit for this area.");
            character.SendErrorMessage(ErrorMessageType.InstanceVisitLimit);
            return false;
        }

        if (_teamDungeons.TryGetValue(team, out var dungeon))
        {
            dungeon ??= [];
        }
        else
        {
            dungeon = [];
            _teamDungeons.Add(team, dungeon);
        }

        var dungeonInstance = new Dungeon(IndunGameData.Instance.GetDungeonZone(ZoneManager.Instance.GetZoneById(zoneId).GroupId), character, team);

        if (dungeonInstance._indunZone is { ItemRequired: > 0 } && !PortalManager.CheckItemAndRemove(character, dungeonInstance._indunZone.ItemRequired, 1))
        {
            Logger.Info("[IndunManager] There is not the right item in the Inventory to visit the area.");
            character.SendErrorMessage(ErrorMessageType.EnterInstReqItem, dungeonInstance._indunZone.ItemRequired);
            return false;
        }

        if (dungeonInstance.IsFull)
        {
            Logger.Info("[IndunManager] Not the right level of character to visit the area.");
            character.SendErrorMessage(ErrorMessageType.InstanceLevel);
            return false;
        }

        if (!(character.Level >= dungeonInstance._indunZone?.LevelMin && character.Level <= dungeonInstance._indunZone?.LevelMax))
        {
            Logger.Info("[IndunManager] Not the right level of character to visit the area.");
            character.SendErrorMessage(ErrorMessageType.InstanceLevel);
            return false;
        }

        if (!CheckingAttempt(dungeonInstance))
        {
            Logger.Info("[IndunManager] The team has exhausted its attendance limit for this area.");
            character.SendErrorMessage(ErrorMessageType.InstanceVisitLimit);
            return false;
        }

        dungeonInstance.AddPlayer(character);
        dungeon.Add(dungeonInstance);
        _teamDungeons[team] = dungeon;
        //character.SendErrorMessage(ErrorMessageType.InstanceInMsgStart);

        return true;
    }

    private bool CreateSoloInstance(Character character, uint zoneId)
    {
        Logger.Info($"[IndunManager] Requesting solo instance, characterId: {character.Id}");
        Logger.Info($"[IndunManager] Total dungeons created... Party: {_teamDungeons.Count}... Solo: {_soloDungeons.Count}...");

        var zoneGroupId = IndunGameData.Instance.GetDungeonZone(ZoneManager.Instance.GetZoneById(zoneId).GroupId).ZoneGroupId;
        if (GetWaitingDungeonAccess(character.Id, zoneGroupId))
        {
            Logger.Info("[IndunManager] Solo dungeon has used up its attendance limit.");
            character.SendErrorMessage(ErrorMessageType.InstanceVisitLimit);
            return false;
        }

        var dungeon = new Dungeon(IndunGameData.Instance.GetDungeonZone(ZoneManager.Instance.GetZoneById(zoneId).GroupId), character, null);

        if (dungeon._indunZone?.ZoneGroupId != ZoneManager.Instance.GetZoneById(zoneId)?.GroupId)
        {
            Logger.Info("[IndunManager] Solo dungeon request on different area. Deleting saved solo dungeon.");
            character.SendErrorMessage(ErrorMessageType.ProhibitedInInstance);
            RequestDeletion(character, dungeon);
            return false;
        }

        if (dungeon._indunZone?.PartyRequired == true)
        {
            Logger.Info("[IndunManager] It is required to be in the party to visit this area. Deleting saved solo dungeon.");
            character.SendErrorMessage(ErrorMessageType.NeedParty);
            //character.SendErrorMessage(ErrorMessageType.CannotFollowNonParty);
            //character.SendErrorMessage(ErrorMessageType.InstanceLeaveParty);
            RequestDeletion(character, dungeon);
            return false;
        }

        if (dungeon._indunZone is { ItemRequired: > 0 } && !PortalManager.CheckItemAndRemove(character, dungeon._indunZone.ItemRequired, 1))
        {
            Logger.Info("[IndunManager] There is not the right item in the Inventory to visit the area. Deleting saved solo dungeon.");
            character.SendErrorMessage(ErrorMessageType.EnterInstReqItem, dungeon._indunZone.ItemRequired);
            RequestDeletion(character, dungeon);
            return false;
        }

        if (!(character.Level >= dungeon._indunZone?.LevelMin && character.Level <= dungeon._indunZone?.LevelMax))
        {
            RequestDeletion(character, dungeon);
            Logger.Info("[IndunManager] Not the right level of character to visit the area. Deleting saved solo dungeon.");
            character.SendErrorMessage(ErrorMessageType.InstanceLevel);
            return false;
        }

        if (dungeon.IsFull)
        {
            RequestDeletion(character, dungeon);
            Logger.Info("[IndunManager] There is no place for you in this area. Deleting saved solo dungeon.");
            character.SendErrorMessage(ErrorMessageType.InstanceQuota);
            return false;
        }

        if (!CheckingAttempt(dungeon))
        {
            Logger.Info("[IndunManager] Solo dungeon has used up its attendance limit.");
            character.SendErrorMessage(ErrorMessageType.InstanceVisitLimit);
            RequestDeletion(character, dungeon);
            return false;
        }

        dungeon.AddPlayer(character);
        _soloDungeons?.Add(character.Id, dungeon);
        //character.SendErrorMessage(ErrorMessageType.InstanceInMsgStart);

        return true;
    }

    private Dungeon CreateSysInstance(Character character, uint zoneId)
    {
        Logger.Info($"[IndunManager] Requesting system instance, characterId: {character.Id}");
        Logger.Info($"[IndunManager] Total dungeons created: Party={_teamDungeons.Count}, Solo={_soloDungeons.Count}, Sys={_sysDungeons.Count}");

        // Если не было инстанса - создадим его
        if (!_sysDungeons.TryGetValue(zoneId, out var dungeon))
        {
            dungeon = new Dungeon(IndunGameData.Instance.GetDungeonZone(ZoneManager.Instance.GetZoneById(zoneId).GroupId), character);
            _sysDungeons.Add(zoneId, dungeon);
        }

        if (dungeon?._indunZone?.ZoneGroupId != ZoneManager.Instance.GetZoneById(zoneId)?.GroupId)
        {
            Logger.Info("[IndunManager] system dungeon request on different area.");
            character.SendErrorMessage(ErrorMessageType.ProhibitedInInstance);
            return null;
        }

        if (!(character.Level >= dungeon?._indunZone?.LevelMin && character.Level <= dungeon._indunZone?.LevelMax))
        {
            Logger.Info("[IndunManager] Not the right level of character to visit the area.");
            character.SendErrorMessage(ErrorMessageType.InstanceLevel);
            return null;
        }

        if (dungeon.IsFull)
        {
            Logger.Info("[IndunManager] There is no place for you in this area.");
            character.SendErrorMessage(ErrorMessageType.InstanceQuota);
            return null;
        }

        return dungeon;
    }

    public bool RequestDeletion(Character character, Dungeon dungeon)
    {
        if (character == null) { return false; }
        if (dungeon == null)
        {
            character.SendErrorMessage(ErrorMessageType.AlreadyUnboundInstance);
            return false;
        }
        if (dungeon.IsSystem) { return false; } // do not delete system dungeons
        if (character.InParty) { return false; }// dungeon for the team will not be deleted.
        if (!dungeon.DestroySoloDungeon(character, dungeon)) { return false; }
        _soloDungeons.Remove(character.Id);

        return true;
    }

    public bool RequestLeave(Character character)
    {
        if (character == null)
            return false;

        if (character.InParty)
        {
            var team = TeamManager.Instance.GetTeamByObjId(character.ObjId);
            if (!_teamDungeons.TryGetValue(team, out var teamDungeon) || teamDungeon is null)
            {
                return false;
            }

            foreach (var dungeon in teamDungeon)
            {
                if (!dungeon.IsPlayerInDungeon(character.Id)) { continue; }

                // событие выхода из TeamDungeon для персонажа
                character.Events.OnDungeonLeave(dungeon, new OnDungeonLeaveArgs { Player = character });
                //dungeon.LeaveInstance(character);
                //character.SendErrorMessage(ErrorMessageType.InstanceInMsgEnd);
                return true;
            }
        }

        foreach (var sysDungeon in _sysDungeons)
        {
            if (sysDungeon.Value.IsPlayerInDungeon(character.Id))
            {
                character.Events.OnDungeonLeave(sysDungeon, new OnDungeonLeaveArgs { Player = character });
                return true;
            }
        }

        if (!_soloDungeons.TryGetValue(character.Id, out var soloDungeon)) { return false; }
        if (soloDungeon is null) { return false; }

        // событие выхода из SoloDungeon для персонажа
        character.Events.OnDungeonLeave(soloDungeon, new OnDungeonLeaveArgs { Player = character });
        //soloDungeon.LeaveInstance(character);
        //character.SendErrorMessage(ErrorMessageType.InstanceInMsgEnd);

        return true;
    }

    public bool RequestSysLeave(Character character)
    {
        if (character == null) { return false; }

        foreach (var dungeon in _sysDungeons.Values.Where(dungeon => dungeon.IsPlayerInDungeon(character.Id)))
        {
            dungeon.LeaveSysInstance(character);
            return true;
        }

        return false;
    }

    public bool SoloToParty(Character character, Team team, Dungeon dungeon)
    {
        if (!_soloDungeons.ContainsKey(character.Id)) { return false; }
        _soloDungeons.Remove(character.Id);
        var dungeonList = new List<Dungeon> { dungeon };
        _teamDungeons.Add(team, dungeonList);

        return true;
    }

    public static void DoIndunActions(uint startActionId, InstanceWorld world)
    {
        while (true)
        {
            var action = IndunGameData.Instance.GetIndunActionById(startActionId);
            action.Execute(world);
            Logger.Warn($"DoIndunActions: world={world.Id}, action.Id={action.Id}, action.NextActionId={action.NextActionId}");
            if (action.NextActionId > 0)
            {
                startActionId = action.NextActionId;
                continue;
            }

            break;
        }
    }

    internal Dungeon GetDungeonByWorldId(uint worldId)
    {
        foreach (var dungeons in _teamDungeons.Values)
        {
            foreach (var dungeon in dungeons.Where(dungeon => dungeon.GetDungeonWorldId() == worldId))
            {
                return dungeon;
            }
        }

        return _soloDungeons.Values.FirstOrDefault(dungeon => dungeon.GetDungeonWorldId() == worldId);
    }

    public void RemoveDungeon(Team team)
    {
        _teamDungeons.Remove(team);
    }

    public void SetRoomCleared(uint indunRoomId, InstanceWorld world)
    {
        var dungeon = GetDungeonByWorldId(world.Id);
        dungeon.SetRoomCleared(indunRoomId);
    }

    public void ClearAttemts(Dungeon dungeon)
    {
        lock (_lock)
        {
            var characterId = dungeon.IsTeamOwned ? dungeon.GetTeamOwner.OwnerId : dungeon.GetCharacterOwner?.Id ?? 0;
            var zoneGroupId = dungeon.GetZoneGroupId;
            if (_waitingDungeonAccessAttemptsCleared.TryGetValue(characterId, out var waitingDungeonAccess))
            {
                waitingDungeonAccess.Remove(zoneGroupId);
            }
        }
    }

    internal bool GetWaitingDungeonAccess(Dungeon dungeon)
    {
        lock (_lock)
        {
            var characterId = dungeon.IsTeamOwned ? dungeon.GetTeamOwner.OwnerId : dungeon.GetCharacterOwner?.Id ?? 0;
            var zoneGroupId = dungeon.GetZoneGroupId;

            if (_waitingDungeonAccessAttemptsCleared.TryGetValue(characterId, out var waitingDungeonAccess))
            {
                if (waitingDungeonAccess.TryGetValue(zoneGroupId, out var wda))
                {
                    return wda;
                }
            }
        }

        return false;
    }
    internal bool GetWaitingDungeonAccess(uint characterId, uint zoneGroupId)
    {
        lock (_lock)
        {
            if (_waitingDungeonAccessAttemptsCleared.TryGetValue(characterId, out var waitingDungeonAccess))
            {
                if (waitingDungeonAccess.TryGetValue(zoneGroupId, out var wda))
                {
                    return wda;
                }
            }
        }

        return false;
    }

    internal void SetWaitingDungeonAccess(uint characterId, uint zoneGroupId, bool value)
    {
        lock (_lock)
        {
            if (_waitingDungeonAccessAttemptsCleared.TryGetValue(characterId, out var waitingDungeonAccess))
            {
                if (waitingDungeonAccess.TryGetValue(zoneGroupId, out _))
                {
                    waitingDungeonAccess[zoneGroupId] = value;
                }
                else
                {
                    var w = new Dictionary<uint, bool>();
                    w.TryAdd(zoneGroupId, value);
                    _waitingDungeonAccessAttemptsCleared.TryAdd(characterId, w);
                }
            }
        }
    }
    internal void SetWaitingDungeonAccess(Dungeon dungeon, bool value)
    {
        lock (_lock)
        {
            var characterId = dungeon.IsTeamOwned ? dungeon.GetTeamOwner.OwnerId : dungeon.GetCharacterOwner?.Id ?? 0;
            var zoneGroupId = dungeon.GetZoneGroupId;

            if (_waitingDungeonAccessAttemptsCleared.TryGetValue(characterId, out var waitingDungeonAccess))
            {
                if (waitingDungeonAccess.TryGetValue(zoneGroupId, out _))
                {
                    waitingDungeonAccess[zoneGroupId] = value;
                }
                else
                {
                    var w = new Dictionary<uint, bool>();
                    w.TryAdd(zoneGroupId, value);
                    _waitingDungeonAccessAttemptsCleared.TryAdd(characterId, w);
                }
            }
        }
    }

    private bool CheckingAttempt(Dungeon dungeon)
    {
        lock (_lock)
        {
            var res = false;
            var characterId = dungeon.IsTeamOwned ? dungeon.GetTeamOwner.OwnerId : dungeon.GetCharacterOwner?.Id ?? 0;
            var zoneGroupId = dungeon.GetZoneGroupId;

            if (GetWaitingDungeonAccess(dungeon))
            {
                return false;
            }

            if (_attempts.TryGetValue(characterId, out var cd))
            {
                if (cd.TryGetValue(zoneGroupId, out _))
                {
                    if (cd[zoneGroupId] >= FreeAttempts)
                    {
                        TickManager.Instance.OnTick.Subscribe(dungeon.WaitingDungeonAccessAttemptsCleared, TimeSpan.FromHours(4), true);
                    }
                    else
                    {
                        res = true;
                        cd[zoneGroupId]++;
                    }
                }
                else
                {
                    res = true;
                    cd = [];
                    cd.TryAdd(zoneGroupId, 1);
                    _attempts.TryAdd(characterId, cd);

                    SetWaitingDungeonAccess(characterId, zoneGroupId, false);
                }
            }
            else
            {
                res = true;
                cd = [];
                cd.TryAdd(zoneGroupId, 1);
                _attempts.TryAdd(characterId, cd);

                SetWaitingDungeonAccess(characterId, zoneGroupId, false);
            }

            return res; // true - еще можно сходить в данжон, false - израсходовали свободные попытки, израсходовали дополнительные попытки
        }
    }

    private void InfoAttempt()
    {
        lock (_lock)
        {
            if (_attempts is { Count: > 0 })
            {
                foreach (var attempt in _attempts)
                {
                    _attempts.TryGetValue(attempt.Key, out var cds);
                    if (cds != null)
                    {
                        foreach (var cd in cds)
                        {
                            Logger.Info($"For player={attempt.Key}: {cd.Value} attempts in dungeon Id={cd.Key}");
                        }
                    }
                }
            }
        }
    }

    public Dungeon GetSoloDungeon(uint characterId)
    {
        _soloDungeons.TryGetValue(characterId, out var dungeon);

        return dungeon;
    }
}
