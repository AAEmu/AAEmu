using System.Runtime.InteropServices;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun.Matching;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.Skills;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class InstantGameManager : Singleton<InstantGameManager>, IInstantGameManager
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<uint, List<MatchmakingApplicant>> _matchmakingQueue = [];

    private readonly List<InstantGame> _instantGames = [];

    private readonly List<InstantGame> _queueList = [];

    private readonly Lock _lock = new();

    /// <summary>Clock behind queue and fill-window stamps; injectable for tests.</summary>
    public Func<DateTime> UtcNow { get; set; } = () => ServerCalendar.UtcNow;

    /// <summary>
    /// Creates a match together with the copy it plays in. Injectable for tests: the production
    /// default builds a real world instance, which unit tests have no world loaded for.
    /// </summary>
    public Func<Battlefield, InstantGame> CreateGame { get; set; } = battlefield => new InstantGame(battlefield);

    public void Initialize()
    {
        // 15 seconds between each matchmaking query
        TickManager.Instance.OnTick.Subscribe(BattlefieldTick, TimeSpan.FromSeconds(15));
    }

    public void ApplyToBattlefield(uint battlefieldId, InstantCorps corps, Character character)
    {
        if (character == null)
            return;

        lock (_lock)
        {
            ref var applicants =
                ref CollectionsMarshal.GetValueRefOrAddDefault(_matchmakingQueue, battlefieldId, out var exists);

            if (!exists)
            {
                if (BattlefieldGameData.Instance.GetBattlefield(battlefieldId) is not null)
                {
                    applicants = [];
                }
                else
                {
                    _log.Warn(
                        "[Matchmaking] Failed to add player {0} to matchmaking queue for battlefield {1}; battlefield does not exist",
                        character.Name, battlefieldId);
                    _matchmakingQueue.Remove(battlefieldId);
                    return;
                }
            }

            if (applicants.Any(applicant => applicant.CharObj == character))
            {
                return;
            }

            applicants.Add(new MatchmakingApplicant(character, UtcNow()));
        }

        _log.Trace("[Matchmaking] Added player " + character.Name + " to matchmaking queue for battlefield " +
                   battlefieldId);

        character.SendPacket(new SCAppliedToInstantGamePacket(battlefieldId));
    }

    public void WithdrawFromBattlefield(Character character)
    {
        // Removes player from matchmaking if they cancel the queue.
        // Player not offline or invalid.

        if (character == null)
            return;

        lock (_lock)
        {
            foreach (var applicants in _matchmakingQueue.Values)
            {
                if (applicants != null)
                {
                    foreach (var player in applicants)
                    {
                        if (player.CharObj == character)
                        {
                            applicants.Remove(player);
                            _log.Trace("[Matchmaking] Removing " + character.Name + " from matchmaking.");
                            return;
                        }
                    }
                }
            }
            // Removes player from an invited game if they decline.
            foreach (var game in _instantGames.ToList())
            {
                if (game.WithdrawPlayer(character))
                {
                    _log.Trace("[Matchmaking] " + character.Name + " declined arena invitation.");
                    if (game.Phase == InstantGamePhase.Filling && !_queueList.Contains(game))
                    {
                        _queueList.Add(game);
                        _log.Trace("[Matchmaking] Adding game to queue list.");
                    }
                    return;
                }
            }
        }

    }

    private void CheckMatchmakingQueue(uint bfId)
    {
        InstantGame game;
        var games = _queueList.Where(o => o.BattlefieldId == bfId).ToList();
        if (!_matchmakingQueue.TryGetValue(bfId, out var applicants))
        {
            return;
        }

        // Remove offline players in the queue list
        var offlinePlayers = applicants.Where(a => a.CharObj == null).ToList();
        if (offlinePlayers.Count > 0)
        {
            foreach (var players in offlinePlayers)
            {
                applicants.Remove(players);
            }
        }

        _log.Trace("[Matchmaking] Running matchmaking for battlefield " + bfId + "... Queue: " + applicants.Count);
        // Check if there are enough players to matchmake a game.
        if (MissingPlayersToStart(bfId, games.Count))
        {
            return;
        }

        // Games without sufficient players are prioritized before new games are made.
        if (games.Count > 0)
        {
            _log.Trace("[Matchmaking] Game found without full players.");
            game = games[0];
        }
        else // Create a new game if there is no current one to matchmake for.
        {
            game = CreateGame(BattlefieldGameData.Instance.GetBattlefield(bfId));
            game.FillingSinceUtc = UtcNow();
        }

        // Loop through the matchmaking list to fill current and new games.
        var queueCount = applicants.Count;
        for (var i = 0; i < queueCount; i++)
        {
            if (game.IsFull)
            {
                _log.Trace("[Matchmaking] Game is full.");
                if (_queueList.Remove(game))
                {
                    _log.Trace("[Matchmaking] Removing queued game from queueList.");
                }
                break; // Matchmaking complete if game is full.
            }

            // Obtain character of player matchmaking and remove them from queue to add them into a game.
            var playerCharacter = WorldManager.Instance.GetCharacterById(applicants[0].CharObj.Id);
            applicants.Remove(applicants[0]);

            // Add player and invite to instant game
            if (playerCharacter != null)
            {
                game.AddPlayer(playerCharacter, game.GetCorps());
                _log.Trace("[Matchmaking] Adding player to game: " + playerCharacter.Name);
            }
            else
            {
                // playerCharacter is null.
            }
        }
        // Add new arena with paired players if there are no queued games to fill up.
        if (!_instantGames.Contains(game) && games.Count == 0)
        {
            _log.Trace("[Matchmaking] Instant game created.");
            _instantGames.Add(game);
        }
        if (game.IsFull)
        {
            _log.Trace("[Matchmaking] Game is full.");
            if (_queueList.Remove(game))
            {
                _log.Trace("[Matchmaking] Removing queued game from queueList.");
            }
        }
        else if (!game.IsFull && bfId != (uint)InstantGameType.Gladiator)
        {
            if (!_queueList.Contains(game))
            {
                // Save the game into queued lists if it is not full yet.
                _log.Trace("[Matchmaking] Adding Drill Camp game to queue list as it is not full.");
                _queueList.Add(game);
            }
        }
    }

    private bool MissingPlayersToStart(uint bfId, int queueCount)
    {
        var bf = BattlefieldGameData.Instance.GetBattlefield(bfId);
        var minimumToStart = queueCount > 0 ? 1 : bf.RuleSet.CorpsSize * 2;
        // Temporary exception to Drill Camp for debugging purposes.
        if (bf.Id == (uint)InstantGameType.DrillCamp)
            return false;

        // There is insufficient players available if queue is not > half of CorpsSize.
        if (_matchmakingQueue[bfId].Count < minimumToStart)
        {
            _log.Trace("[Matchmaking] " + (minimumToStart - _matchmakingQueue[bfId].Count) + " player(s) are missing to allow matchmaking for battlefield " + bfId);
            return true;
        }
        return false;
    }

    public bool PlayerCanEnter(Character character)
    {
        if (character != null)
        {
            if (character.IsInBattle)
                return false; // In Combat
            else if (character.Transform.InstanceId != WorldManager.DefaultInstanceId)
                return false; // In an instanced world (Dungeon or Mirage)
            else if (character.IsDead)
                return false; // Is dead
            else if (character.IsInDuel)
                return false; // In a duel
            else if (character.Buffs.CheckBuff((uint)BuffConstants.Arrested))
                return false; // Under arrest
            else if (JusticeManager.IsPrisoner(character))
                return false; // Serving a sentence
            else if (character.Buffs.CheckBuff((uint)BuffConstants.Juror))
                return false; // Serving on a jury
            else if (character.Inventory.Equipment.GetItemBySlot(26) != null)
                return false; // Tradepack equipped
            else if (character.Buffs.CheckBuff(2385))
                return false; // Rebirth trauma is active
            else if (WorldManager.Instance.GetWorld(character.Transform.InstanceId)?.SlaveManager.GetActiveSlaveByOwnerObjId(character.ObjId) != null)
                return false; // Vehicle or boat is summoned
            else
                return true;
        }
        return false; // Character is null
    }

    public void BattlefieldTick(TimeSpan delta)
    {
        var now = UtcNow();
        List<Action> deferred = [];
        lock (_lock)
        {
            ExpireQueuedApplicants(now, deferred);

            foreach (var bfId in _matchmakingQueue.Keys.ToList())
            {
                CheckMatchmakingQueue(bfId);
            }

            ReapUnfilledGames(now, deferred);
        }

        // Packets and teardowns leave the lock: releasing a player re-enters this manager
        // (RemoveGame), and world teardown has no business running under the queue lock.
        foreach (var action in deferred)
            action();
    }

    /// <summary>
    /// A queue that never fills releases everybody: each expired applicant is dropped from the
    /// queue and acked with the queue-clear cancel, so no one sits on a screen for a match that is
    /// no longer coming. Expiry window is the battle field's own
    /// <c>instances.apply_waiting_time</c> (0 disables it); the timing rule itself is the same one
    /// the Indun queue uses.
    /// </summary>
    private void ExpireQueuedApplicants(DateTime now, List<Action> deferred)
    {
        foreach (var (battlefieldId, applicants) in _matchmakingQueue.ToList())
        {
            var waitingTimeMs = BattlefieldGameData.Instance.GetBattlefield(battlefieldId)?.ApplyWaitingTimeMs ?? 0u;
            foreach (var applicant in applicants.ToList())
            {
                if (!IndunMatchReadyRules.IsQueueExpired(applicant.TimeApplied, now, waitingTimeMs))
                    continue;

                applicants.Remove(applicant);
                var character = applicant.CharObj;
                deferred.Add(() =>
                {
                    character?.SendPacket(SCCancelInstantGamePacket.ClearQueue());
                    _log.Info("[Matchmaking] Queue expired char={0} battlefield={1}",
                        character?.Name, battlefieldId);
                });
            }

            if (applicants.Count == 0)
                _matchmakingQueue.Remove(battlefieldId);
        }
    }

    /// <summary>
    /// A match that is still filling when its cleanup term runs out is abandoned: everyone
    /// attached is released through the leave path (entered players returned to the world,
    /// dialog-only players get their invite cleared) and the copy is disposed. Empty fillers go
    /// immediately — there is nothing left to wait for.
    /// </summary>
    private void ReapUnfilledGames(DateTime now, List<Action> deferred)
    {
        foreach (var game in _instantGames.ToList())
        {
            if (game.Phase != InstantGamePhase.Filling && game.Phase != InstantGamePhase.Opening)
                continue;

            var empty = game.PlayerCount == 0 && game.Phase == InstantGamePhase.Filling;
            var cleanupTermMs = BattlefieldGameData.Instance.GetBattlefield(game.BattlefieldId)?.MatchingCleanupTermMs ?? 0u;
            var since = game.Phase == InstantGamePhase.Opening ? game.OpeningSinceUtc : game.FillingSinceUtc;
            var expired = IndunMatchReadyRules.IsInviteExpired(since, now, cleanupTermMs);
            if (!empty && !expired)
                continue;

            _instantGames.Remove(game);
            _queueList.Remove(game);
            _log.Info("[Matchmaking] Expiring unfilled match battlefield={0} players={1} expired={2}",
                game.BattlefieldId, game.PlayerCount, expired);
            deferred.Add(game.AbandonFilling);
        }
    }

    public void RemoveGame(InstantGame game)
    {
        lock (_lock)
        {
            _instantGames.Remove(game);
            _queueList.Remove(game);
        }
    }

    /// <summary>
    /// A crash or logout has to give back everything the character still holds: its slot in the
    /// matchmaking queue and any per-player match state (roster entries, the kill subscription,
    /// the match back-reference). No packets are sent on this path — the connection is going away.
    /// Squad instance flags recover through the squad login/list recovery, the same way they do
    /// after an instance disconnect.
    /// </summary>
    public void OnCharacterLogout(Character character)
    {
        if (character == null)
            return;

        List<InstantGame> matches = [];
        var queued = false;
        lock (_lock)
        {
            foreach (var applicants in _matchmakingQueue.Values)
                queued |= applicants.RemoveAll(applicant => applicant.CharObj == character) > 0;

            foreach (var game in _instantGames)
                if (game.ContainsPlayer(character))
                    matches.Add(game);
        }

        foreach (var game in matches)
            game.ReleasePlayer(character);

        if (queued || matches.Count > 0)
            _log.Info("[Matchmaking] Released disconnected char={0} queued={1} matches={2}",
                character.Name, queued, matches.Count);
    }

    // Introspection for tests: state assertions without reaching into the collections.
    internal int GetQueueCount(uint battlefieldId)
    {
        lock (_lock)
            return _matchmakingQueue.TryGetValue(battlefieldId, out var applicants) ? applicants.Count : 0;
    }

    internal bool IsQueued(Character character)
    {
        lock (_lock)
            return _matchmakingQueue.Values.Any(applicants =>
                applicants.Any(applicant => applicant.CharObj == character));
    }

    internal bool IsTrackedGame(InstantGame game)
    {
        lock (_lock)
            return _instantGames.Contains(game);
    }
}
