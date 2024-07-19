using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Duels;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Duels;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class DuelManager : Singleton<DuelManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private DoodadSpawner _combatFlag;
    private const double Delay = 1000; // 1 sec
    private const float DistanceForSurrender = 75; // square 75 meters
    private const double DuelDurationTime = 5;    // 5 min

    // there can be several duels at the same time
    private ConcurrentDictionary<uint, Duel> _duels;
    public Dictionary<uint, uint> _saveFactions { get; set; }

    protected DuelManager()
    {
        _duels = new ConcurrentDictionary<uint, Duel>();
        _saveFactions = new Dictionary<uint, uint>();
    }

    public static bool Initialize()
    {
        Logger.Info("Initialising Duel Manager...");
        return true;
    }

    private void DuelAdd(Duel duel)
    {
        if (!_duels.ContainsKey(duel.Challenger.Id))
            _duels.TryAdd(duel.Challenger.Id, duel);
        if (!_duels.ContainsKey(duel.Challenged.Id))
            _duels.TryAdd(duel.Challenged.Id, duel);
    }

    private void DuelRemove(Duel duel)
    {
        _duels.TryRemove(duel.Challenger.Id, out _);
        _duels.TryRemove(duel.Challenged.Id, out _);
    }

    public void DuelRequest(Character challenger, uint challengedId)
    {
        // приходит ID того, кого вызвали на дуэль
        var challenged = WorldManager.Instance.GetCharacterById(challengedId);
        var duel = new Duel(challenger, challenged);
        DuelAdd(duel);
        challenged.SendPacket(new SCDuelChallengedPacket(challenger.Id)); // we send only to the enemy
        Logger.Warn($"DuelRequest: challenger={challenger.Id}:{challenger.ObjId}, challenged={challengedId}:{challenged.Id}:{challenged.ObjId}");
    }

    public void DuelAccepted(Character challenged, uint challengerId)
    {
        if (challenged == null)
        {
            throw new ArgumentNullException(nameof(challenged));
        }
        // приходит ID того, кто вызвал на дуэль
        try
        {
            var duel = _duels[challengerId];

            if (duel.DuelStarted == false)
            {
                duel.DuelStarted = true;
                duel.Challenger.IsInDuel = true;
                duel.Challenged.IsInDuel = true;

                // spawn flag
                _combatFlag = new DoodadSpawner();
                _combatFlag.Id = 0;
                _combatFlag.UnitId = 5014; // Combat Flag Id=5014;
                _combatFlag.Position = duel.Challenger.Transform.CloneAsSpawnPosition();
                _combatFlag.Position.X = duel.Challenger.Transform.World.Position.X - (duel.Challenger.Transform.World.Position.X - duel.Challenged.Transform.World.Position.X) / 2;
                _combatFlag.Position.Y = duel.Challenger.Transform.World.Position.Y - (duel.Challenger.Transform.World.Position.Y - duel.Challenged.Transform.World.Position.Y) / 2;
                _combatFlag.Position.Z = AppConfiguration.Instance.HeightMapsEnable
                    ? WorldManager.Instance.GetHeight(_combatFlag.Position.ZoneId, _combatFlag.Position.X, _combatFlag.Position.Y)
                    : duel.Challenger.Transform.World.Position.Z - (duel.Challenger.Transform.World.Position.Z - duel.Challenged.Transform.World.Position.Z) / 2;

                duel.DuelFlag = _combatFlag.Spawn(0); // set CombatFlag

                // change the faction temporarily
                SetFaction(duel.Challenger, FactionsEnum.RedTeam);
                SetFaction(duel.Challenged, FactionsEnum.BlueTeam);

                //Schedule duel start task.
                duel.DuelStartTask = new DuelStartTask(duel.Challenger.Id);
                TaskManager.Instance.Schedule(duel.DuelStartTask, TimeSpan.FromSeconds(3));
            }
            else
                Logger.Warn($"DuelAccepted: Duel with challengerId = {challengerId} is already started");
        }
        catch (Exception e)
        {
            // id is missing in the database
            Logger.Warn($"DuelAccepted: Id = {challengerId} not found in duels[], error code: {e}");
        }
    }

    private void SetFaction(Unit ower, uint factionId)
    {
        // change the faction temporarily
        if (_saveFactions.ContainsKey(ower.Id))
        {
            _saveFactions[ower.Id] = ower.Faction.Id;
        }
        else
        {
            _saveFactions.Add(ower.Id, ower.Faction.Id);
        }

        ower.SetFaction(factionId);
    }

    private void RestoreFaction(Unit owner)
    {
        // restore the fraction
        owner.SetFaction(_saveFactions[owner.Id]);
        _saveFactions.Remove(owner.Id);
    }

    public void DuelStart(uint id)
    {
        try
        {
            var duel = _duels[id];
            duel.SendPacketsBoth(new SCDuelStartedPacket(duel.Challenger.ObjId, duel.Challenged.ObjId));
            duel.SendPacketsBoth(new SCAreaChatBubblePacket(true, duel.Challenger.ObjId, 543));
            //duel.SendPacketChallenger(new SCAreaChatBubblePacket(true, duel.Challenged.ObjId, 543));
            duel.SendPacketsBoth(new SCDuelStartCountdownPacket());
            duel.SendPacketsBoth(new SCDuelStatePacket(duel.Challenger.ObjId, duel.DuelFlag.ObjId));
            duel.SendPacketsBoth(new SCDuelStatePacket(duel.Challenged.ObjId, duel.DuelFlag.ObjId));
            // make the flag flutter in the wind
            duel.SendPacketChallenger(new SCDoodadPhaseChangedPacket(_combatFlag.Last));
            // Player can be attacked
            duel.SendPacketsBoth(new SCCombatEngagedPacket(duel.Challenger.ObjId));
            duel.SendPacketsBoth(new SCCombatEngagedPacket(duel.Challenged.ObjId));

            // final operations after a duel
            duel.DuelEndTimerTask = new DuelEndTimerTask(duel, duel.Challenger.Id);
            TaskManager.Instance.Schedule(duel.DuelEndTimerTask, TimeSpan.FromMinutes(DuelDurationTime));

            // запустим проверку на дистанцию
            _ = DuelDistanceСheck(duel.Challenger.Id);

            // запустим проверку на количество жизни
            _ = DuelResultСheck(duel.Challenger.Id);
        }
        catch (Exception e)
        {
            // id is missing in the database
            Logger.Warn($"DuelStart: Id = {id} not found in duels[], error code: {e}");
        }
    }

    public void DuelCancel(uint challengerId, ErrorMessageType errorMessage)
    {
        try
        {
            var duel = _duels[challengerId];
            duel.DuelAllowed = false;
            if (errorMessage != 0)
                duel.Challenger.SendErrorMessage(errorMessage);

            Logger.Warn($"DuelCancel: Duel with challengerId={challengerId} canceled, error={errorMessage}");
            DuelCleanUp(challengerId);
        }
        catch (Exception e)
        {
            // id is missing in the database
            Logger.Warn($"DuelCancel: Id={challengerId} not found in duels[], error code: {e}");
        }
    }

    private void DuelCleanUp(uint id)
    {
        try
        {
            var duel = _duels[id];

            duel.Challenger.IsInDuel = false;
            duel.Challenged.IsInDuel = false;

            if (duel.DuelStartTask != null)
            {
                _ = duel.DuelStartTask.Cancel();
                duel.DuelStartTask = null;
            }

            if (duel.DuelEndTimerTask != null)
            {
                _ = duel.DuelEndTimerTask.Cancel();
                duel.DuelEndTimerTask = null;
            }

            DuelRemove(duel);
        }
        catch (Exception e)
        {
            // id is missing in the database
            Logger.Warn($"CleanUpDuel: Id={id} not found in duels[], error code: {e}");
        }
    }

    public void DuelStop(uint id, DuelDetType det, uint loseId = 0)
    {
        try
        {
            Logger.Warn("DuelStop: Duel ended");
            var duel = _duels[id];
            duel.DuelAllowed = false;
            // Duel is over, det 00=lose, 01=win, 02=surrender (Fled beyond the flag action border), 03=draw
            if (det == DuelDetType.Draw)
            {
                duel.SendPacketChallenged(new SCDuelEndedPacket(duel.Challenger.Id, duel.Challenged.Id, duel.Challenger.ObjId, duel.Challenged.ObjId, det));
                duel.SendPacketChallenger(new SCDuelEndedPacket(duel.Challenged.Id, duel.Challenger.Id, duel.Challenged.ObjId, duel.Challenger.ObjId, det));
                Logger.Warn("DuelStop: Draw!");
            }
            else if (loseId != 0)
            {
                if (loseId == duel.Challenger.Id)
                {
                    duel.SendPacketsBoth(new SCDuelEndedPacket(duel.Challenged.Id, duel.Challenger.Id, duel.Challenged.ObjId, duel.Challenger.ObjId, det));
                    Logger.Warn($"DuelStop: Challenger:{duel.Challenger.Name} Lose, Challenged:{duel.Challenged.Name} Win!");
                }
                else if (loseId == duel.Challenged.Id)
                {
                    duel.SendPacketsBoth(new SCDuelEndedPacket(duel.Challenger.Id, duel.Challenged.Id, duel.Challenger.ObjId, duel.Challenged.ObjId, det));
                    Logger.Warn($"DuelStop: Challenger:{duel.Challenger.Name} Win, Challenged:{duel.Challenged.Name} Lose!");
                }
            }
            // Duel Status - Duel ended
            duel.SendPacketsBoth(new SCDuelStatePacket(duel.Challenged.ObjId, 0));
            duel.SendPacketsBoth(new SCDuelStatePacket(duel.Challenger.ObjId, 0));

            if (duel.DuelFlag != null)
            {
                duel.DuelFlag.Delete(); //Remove Flag
                // Remove Flag
                duel.SendPacketsBoth(new SCDoodadRemovedPacket(duel.DuelFlag.ObjId));
            }

            // restore the fraction
            RestoreFaction(duel.Challenger);
            RestoreFaction(duel.Challenged);

            // Player cannot be attacked
            duel.Challenger.IsInBattle = false;
            duel.Challenged.IsInBattle = false;

            DuelCleanUp(id);
        }
        catch (Exception e)
        {
            // id is missing in the database
            Logger.Warn($"DuelStop: Id={id} not found in duels[], error code: {e}");
        }
    }

    public bool DuelResultСheck(uint id)
    {
        try
        {
            var duel = _duels[id];
            if (duel.Challenger.Hp <= 1 || duel.Challenged.Hp <= 1)
            {
                duel.DuelResultСheckTask.Cancel();
                duel.DuelResultСheckTask = null;
                return true;
            }

            duel.DuelResultСheckTask = new DuelResultСheckTask(duel);
            TaskManager.Instance.Schedule(duel.DuelResultСheckTask, TimeSpan.FromMilliseconds(Delay));
        }
        catch (Exception e)
        {
            // id is missing in the database
            Logger.Warn($"DuelResultСheck: Id={id} not found in duels[], error code: {e}");
            return false;
        }
        return false;
    }

    public DuelDistance DuelDistanceСheck(uint id)
    {
        try
        {
            var duel = _duels[id];
            // проверяем, сбежали от флага или нет
            var currentDistance = MathUtil.CalculateDistance(duel.DuelFlag.Transform.World.Position, duel.Challenger.Transform.World.Position, true);
            if (currentDistance >= DistanceForSurrender)
            {
                // отключаем таймер
                if (duel.DuelDistanceСheckTask == null)
                    return DuelDistance.ChallengerFar; // сдается тот, кто вызывал на дуэль, т.е. убежал от флага

                _ = duel.DuelDistanceСheckTask.Cancel();
                duel.DuelDistanceСheckTask = null;
                return DuelDistance.ChallengerFar; // сдается тот, кто вызывал на дуэль, т.е. убежал от флага
            }
            // проверяем, сбежали от флага или нет
            currentDistance = MathUtil.CalculateDistance(duel.DuelFlag.Transform.World.Position, duel.Challenged.Transform.World.Position, true);
            if (currentDistance >= DistanceForSurrender)
            {
                // отключаем таймер
                if (duel.DuelDistanceСheckTask == null)
                    return DuelDistance.ChallengedFar; // сдается тот, кого вызвали на дуэль, т.е. убежал от флага

                _ = duel.DuelDistanceСheckTask.Cancel();
                duel.DuelDistanceСheckTask = null;
                return DuelDistance.ChallengedFar; // сдается тот, кого вызвали на дуэль, т.е. убежал от флага
            }

            duel.DuelDistanceСheckTask = new DuelDistanceСheckTask(duel);
            TaskManager.Instance.Schedule(duel.DuelDistanceСheckTask, TimeSpan.FromMilliseconds(Delay));
        }
        catch (Exception e)
        {
            // id is missing in the database
            Logger.Warn($"DistanceСheck: Id={id} not found in duels[], error code: {e}");
            return DuelDistance.Error;  // рядом с флагом
        }
        return DuelDistance.Near;  // рядом с флагом
    }
}
