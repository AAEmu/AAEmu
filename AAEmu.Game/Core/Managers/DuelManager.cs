using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
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

public class DuelManager : Singleton<DuelManager>, IDuelManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private DoodadSpawner _combatFlag;
    private const double Delay = 1000; // 1 sec
    private const float DistanceForSurrender = 75; // square 75 meters
    private const double DuelDurationTime = 5;    // 5 min

    /// <summary>
    /// How long the client counts down before a duel begins. Not our choice: its countdown handler
    /// (RVA 0x105E20) writes the constant 0xBB8 = 3000 ms, so anything else here would put our start
    /// packet out of step with what the player is watching.
    /// </summary>
    private static readonly TimeSpan CountdownDuration = TimeSpan.FromMilliseconds(3000);

    /// <summary>
    /// How long an unanswered invitation keeps both players reserved. The client has no timer of its
    /// own - its challenge handler only builds the dialog - so if we do not expire the request nobody
    /// will, and an ignored popup blocks both players from duelling for the rest of the session.
    /// </summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    // there can be several duels at the same time
    private readonly ConcurrentDictionary<uint, Duel> _duels = new();
    public Dictionary<uint, FactionsEnum> SaveFactions { get; set; } = [];

    public void Initialize()
    {
        Logger.Info("Initialising Duel Manager...");
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

    public void DuelRequest(Character challenger, uint challengedId, byte duelType = 0)
    {
        if (challenger == null)
            return;

        // The target used to be taken on trust: an unknown id produced a Duel with a null Challenged,
        // and DuelAdd then threw on its Id, leaving a half-registered duel behind.
        var challenged = WorldManager.Instance.GetCharacterById(challengedId);
        if (challenged == null)
        {
            challenger.SendErrorMessage(ErrorMessageType.BadDuelTarget);
            Logger.Warn($"DuelRequest: challenged id {challengedId} is not online");
            return;
        }

        if (challenged.Id == challenger.Id)
        {
            challenger.SendErrorMessage(ErrorMessageType.BadDuelSelf);
            return;
        }

        // Neither side may already be involved. Without this the table kept an entry from every earlier
        // request - including ones nobody ever answered - and both players stayed stuck as "already in
        // a duel" with no way out short of a server restart. Both error messages already existed in
        // ErrorMessageType and were never used by anything.
        if (_duels.ContainsKey(challenger.Id))
        {
            challenger.SendErrorMessage(ErrorMessageType.AlreadyInDuel);
            return;
        }

        if (_duels.ContainsKey(challenged.Id))
        {
            challenger.SendErrorMessage(ErrorMessageType.OtherAlreadyInDuel);
            return;
        }

        // The client discards SCDuelStarted when the duel type is 0, so a request that arrives without
        // one would produce a duel that silently never begins. Fall back to a normal 1v1.
        if (duelType == 0)
            duelType = Duel.NormalDuel;

        var duel = new Duel(challenger, challenged, duelType);
        DuelAdd(duel);

        // Both players are now reserved. Arm the release before the invitation goes out, so that an
        // ignored popup cannot leave them reserved for good.
        duel.DuelRequestTimeoutTask = new DuelRequestTimeoutTask(challenger.Id);
        TaskManager.Instance.Schedule(duel.DuelRequestTimeoutTask, RequestTimeout);

        challenged.SendPacket(new SCDuelChallengedPacket(challenger.Id, duelType)); // we send only to the enemy
        Logger.Info($"DuelRequest: challenger={challenger.Id}:{challenger.ObjId}, challenged={challenged.Id}:{challenged.ObjId}, type={duelType}");
    }

    /// <summary>
    /// Called when nobody answered an invitation within <see cref="RequestTimeout"/>. Releases both
    /// players. A duel that has already been accepted is left alone - it has its own end conditions.
    /// </summary>
    public void DuelRequestExpired(uint challengerId)
    {
        if (!_duels.TryGetValue(challengerId, out var duel))
            return;

        if (duel.DuelStarted)
            return;

        Logger.Info($"DuelRequestExpired: {duel.Challenger.Name} -> {duel.Challenged.Name} went unanswered, releasing both");
        duel.Challenger.SendErrorMessage(ErrorMessageType.TargetRejectedDuel);
        ReleaseDuel(duel);
    }

    /// <summary>
    /// Cancels whatever duel a character is involved in when they leave the world.
    /// </summary>
    /// <remarks>
    /// Logging out used to release nothing: the entry, and with it the reference to the Character, sat
    /// in the table until the server restarted, and the player was refused every duel after their next
    /// login. A duel already in progress is ended as cancelled so the opponent gets a result and their
    /// faction and flag are cleaned up.
    /// </remarks>
    public void OnCharacterLogout(Character character)
    {
        if (character == null || !_duels.TryGetValue(character.Id, out var duel))
            return;

        if (duel.DuelStarted)
        {
            Logger.Info($"OnCharacterLogout: {character.Name} left during a duel, cancelling it");
            DuelStop(character.Id, DuelDetType.Cancel, character.Id);
            return;
        }

        // Still only an invitation. Tell whoever is left that it is off.
        var other = duel.Challenger.Id == character.Id ? duel.Challenged : duel.Challenger;
        Logger.Info($"OnCharacterLogout: {character.Name} left with a duel invitation pending, releasing both");
        other.SendErrorMessage(ErrorMessageType.TargetRejectedDuel);
        ReleaseDuel(duel);
    }

    /// <summary>
    /// Frees both players from a duel that never started: cancel the timeout and drop the entry.
    /// </summary>
    private void ReleaseDuel(Duel duel)
    {
        if (duel.DuelRequestTimeoutTask != null)
        {
            _ = duel.DuelRequestTimeoutTask.Cancel();
            duel.DuelRequestTimeoutTask = null;
        }

        DuelRemove(duel);
    }

    public void DuelAccepted(Character challenged, uint challengerId)
    {
        ArgumentNullException.ThrowIfNull(challenged);
        // приходит ID того, кто вызвал на дуэль
        try
        {
            // The invitation can have expired while the popup sat on screen, so accepting one we no
            // longer know about is normal rather than exceptional - say so instead of failing mutely.
            if (!_duels.TryGetValue(challengerId, out var duel))
            {
                challenged.SendErrorMessage(ErrorMessageType.BadDuelTarget);
                Logger.Info($"DuelAccepted: no pending duel for challenger {challengerId} - expired or withdrawn");
                return;
            }

            if (duel.DuelStarted == false)
            {
                duel.DuelStarted = true;
                duel.Challenger.IsInDuel = true;
                duel.Challenged.IsInDuel = true;

                // Answered in time - the reservation is now the duel's own business.
                if (duel.DuelRequestTimeoutTask != null)
                {
                    _ = duel.DuelRequestTimeoutTask.Cancel();
                    duel.DuelRequestTimeoutTask = null;
                }

                // spawn flag
                _combatFlag = new DoodadSpawner
                {
                    ParentWorld = challenged.ParentWorld, Id = 0, UnitId = 5014, // Combat Flag Id=5014;
                    Position = duel.Challenger.Transform.CloneAsSpawnPosition()
                };
                _combatFlag.Position.X = duel.Challenger.Transform.World.Position.X - (duel.Challenger.Transform.World.Position.X - duel.Challenged.Transform.World.Position.X) / 2;
                _combatFlag.Position.Y = duel.Challenger.Transform.World.Position.Y - (duel.Challenger.Transform.World.Position.Y - duel.Challenged.Transform.World.Position.Y) / 2;
                _combatFlag.Position.Z = challenged.ParentWorld.Template.GeoData.GetHeight(_combatFlag.Position.AsPositionVector());

                duel.DuelFlag = _combatFlag.Spawn(0); // set CombatFlag

                // change the faction temporarily
                SetFaction(duel.Challenger, FactionsEnum.RedTeam);
                SetFaction(duel.Challenged, FactionsEnum.BlueTeam);

                // Start the client's countdown NOW, not together with the duel itself. The client runs
                // it for a fixed 3000 ms of its own (see SCDuelStartCountdownPacket), so the start task
                // below has to wait exactly that long - otherwise the countdown and the "fight" cue
                // land in the same frame and the player never sees one.
                duel.SendPacketsBoth(new SCDuelStartCountdownPacket());

                //Schedule duel start task.
                duel.DuelStartTask = new DuelStartTask(duel.Challenger.Id);
                TaskManager.Instance.Schedule(duel.DuelStartTask, CountdownDuration);
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

    private void SetFaction(Unit ower, FactionsEnum factionId)
    {
        // change the faction temporarily
        if (SaveFactions.ContainsKey(ower.Id))
        {
            SaveFactions[ower.Id] = ower.Faction.Id;
        }
        else
        {
            SaveFactions.Add(ower.Id, ower.Faction.Id);
        }

        ower.SetFaction(factionId);
    }

    private static void RelayDuelStateToZone(Character character)
    {
        if (!WorldIntegration.ZoneAuthority)
            return;

        WorldIntegration.RelayUnitDuelStateToZone?.Invoke(
            character.ObjId,
            character.DuelStateObjectId,
            character.DuelTeamType);
    }

    private void RestoreFaction(Unit owner)
    {
        // A duel that never got as far as swapping factions has nothing saved here. Indexing blindly
        // threw, and since this runs in the middle of winding the duel down, that throw used to take
        // the release of both players with it.
        if (!SaveFactions.Remove(owner.Id, out var faction))
            return;

        owner.SetFaction(faction);
    }

    public void DuelStart(uint id)
    {
        try
        {
            var duel = _duels[id];

            // Each side is told who it is fighting, so the two packets are not the same - and the duel
            // type has to travel with them or the client drops both. The countdown was sent three
            // seconds ago, when the challenge was accepted.
            duel.SendPacketChallenger(new SCDuelStartedPacket(duel.Challenged.ObjId, duel.DuelType));
            duel.SendPacketChallenged(new SCDuelStartedPacket(duel.Challenger.ObjId, duel.DuelType));

            duel.SendPacketsBoth(new SCAreaChatBubblePacket(true, duel.Challenger.ObjId, 543));
            //duel.SendPacketChallenger(new SCAreaChatBubblePacket(true, duel.Challenged.ObjId, 543));
            duel.Challenger.DuelStateObjectId = duel.DuelFlag.ObjId;
            duel.Challenged.DuelStateObjectId = duel.DuelFlag.ObjId;
            duel.SendPacketsBoth(new SCDuelStatePacket(
                duel.Challenger.ObjId,
                duel.Challenger.DuelStateObjectId,
                unchecked((sbyte)duel.Challenger.DuelTeamType)));
            duel.SendPacketsBoth(new SCDuelStatePacket(
                duel.Challenged.ObjId,
                duel.Challenged.DuelStateObjectId,
                unchecked((sbyte)duel.Challenged.DuelTeamType)));
            RelayDuelStateToZone(duel.Challenger);
            RelayDuelStateToZone(duel.Challenged);
            // make the flag flutter in the wind
            duel.SendPacketChallenger(new SCDoodadPhaseChangedPacket(_combatFlag.Last));
            // Player can be attacked
            duel.SendPacketsBoth(new SCCombatEngagedPacket(duel.Challenger.ObjId, duel.Challenged.ObjId));

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
        if (!_duels.TryGetValue(id, out var duel))
            return;

        try
        {
            duel.Challenger.IsInDuel = false;
            duel.Challenged.IsInDuel = false;

            // Every task has to go, not just the two that used to be listed here. The distance and
            // result checks reschedule themselves once a second and each one holds the Duel - and with
            // it both Characters - alive until it next fails to find the duel in the table.
            duel.DuelRequestTimeoutTask = CancelTask(duel.DuelRequestTimeoutTask);
            duel.DuelStartTask = CancelTask(duel.DuelStartTask);
            duel.DuelEndTimerTask = CancelTask(duel.DuelEndTimerTask);
            duel.DuelDistanceСheckTask = CancelTask(duel.DuelDistanceСheckTask);
            duel.DuelResultСheckTask = CancelTask(duel.DuelResultСheckTask);
        }
        catch (Exception e)
        {
            Logger.Error($"CleanUpDuel: Id={id} threw while tidying up, dropping the entry anyway: {e}");
        }
        finally
        {
            DuelRemove(duel);
        }
    }

    /// <summary>Cancels a scheduled task if there is one, and hands back the null to store.</summary>
    private static T CancelTask<T>(T task) where T : Models.Tasks.Task
    {
        if (task != null)
            _ = task.Cancel();

        return null;
    }

    public void DuelStop(uint id, DuelDetType det, uint loseId = 0)
    {
        if (!_duels.TryGetValue(id, out var duel))
        {
            Logger.Warn($"DuelStop: Id={id} not found in duels[]");
            return;
        }

        // Winding a duel down touches players who may already be halfway out of the world, so any of
        // the steps below can throw. Releasing them is the one thing that must happen regardless -
        // DuelCleanUp used to sit last inside the try, where a single throw above it skipped the
        // release and left both players registered as duelling until the server restarted.
        try
        {
            duel.DuelAllowed = false;

            // A decided duel needs somebody to have lost it. Without that we cannot say who won, and
            // announcing a winner to both sides is worse than calling it a draw.
            if (det == DuelDetType.Decided && loseId == 0)
            {
                Logger.Warn($"DuelStop: Id={id} decided but no loser given, reporting a draw");
                det = DuelDetType.Draw;
            }

            SendDuelEnded(duel, det, loseId);
            Logger.Info($"DuelStop: {duel.Challenger.Name} vs {duel.Challenged.Name}, det={det}, loser={loseId}");

            // Duel Status - Duel ended
            duel.Challenged.DuelStateObjectId = 0;
            duel.Challenger.DuelStateObjectId = 0;
            duel.SendPacketsBoth(new SCDuelStatePacket(
                duel.Challenged.ObjId,
                duel.Challenged.DuelStateObjectId,
                unchecked((sbyte)duel.Challenged.DuelTeamType)));
            duel.SendPacketsBoth(new SCDuelStatePacket(
                duel.Challenger.ObjId,
                duel.Challenger.DuelStateObjectId,
                unchecked((sbyte)duel.Challenger.DuelTeamType)));
            RelayDuelStateToZone(duel.Challenged);
            RelayDuelStateToZone(duel.Challenger);

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
        }
        catch (Exception e)
        {
            Logger.Error($"DuelStop: Id={id} did not wind down cleanly, releasing both players anyway: {e}");
        }
        finally
        {
            DuelCleanUp(id);
        }
    }

    /// <summary>
    /// Announces the outcome to both duellists. SCDuelEnded carries an isWin flag and a list of
    /// opponents, both from the receiving player's point of view, so the two sides get different
    /// packets rather than one broadcast.
    /// </summary>
    private static void SendDuelEnded(Duel duel, DuelDetType det, uint loseId)
    {
        SendDuelEndedTo(duel.Challenger, duel.Challenged, det, loseId);
        SendDuelEndedTo(duel.Challenged, duel.Challenger, det, loseId);
    }

    private static void SendDuelEndedTo(Character receiver, Character opponent, DuelDetType det, uint loseId)
    {
        // isWin only selects a text when the duel was decided; for a draw and for a cancelled duel the
        // client's win and lose tables hold the same string, so the flag makes no difference there.
        var isWin = det == DuelDetType.Decided && loseId != receiver.Id;

        receiver.SendPacket(new SCDuelEndedPacket(isWin, det, [opponent.ObjId], [opponent.Id]));
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
