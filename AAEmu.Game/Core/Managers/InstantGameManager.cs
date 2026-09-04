using System.Runtime.InteropServices;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.InstantGame.Static;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class InstantGameManager : Singleton<InstantGameManager>, IInstantGameManager
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<uint, List<MatchmakingApplicant>> _matchmakingQueue = [];

    private readonly List<InstantGame> _instantGames = [];

    private readonly List<InstantGame> _queueList = [];

    private readonly Lock _lock = new();

    public void Initialize()
    {
        // 15 seconds between each matchmaking query
        TickManager.Instance.OnTick.Subscribe(BattlefieldTick, TimeSpan.FromSeconds(15));
    }

    public void ApplyToBattlefield(uint battlefieldId, InstantCorps corps, Character character)
    {
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

            applicants.Add(new MatchmakingApplicant(character));
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
            foreach (var game in _instantGames)
            {
                if (game.RemovePlayer(character))
                {
                    _log.Trace("[Matchmaking] " + character.Name + " declined arena invitation.");
                    if (!_queueList.Contains(game))
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
            game = new InstantGame(BattlefieldGameData.Instance.GetBattlefield(bfId));

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
            // TODO: Not in jury, not in duel, not in jail conditions
            if (character.IsInBattle)
                return false; // In Combat
            else if (character.Transform.InstanceId != WorldManager.DefaultInstanceId)
                return false; // In an instanced world (Dungeon or Mirage)
            else if (character.IsDead)
                return false; // Is dead
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
        lock (_lock)
        {
            foreach (var (bfId, players) in _matchmakingQueue)
            {
                CheckMatchmakingQueue(bfId);
            }
        }
    }

    public void RemoveGame(InstantGame game)
    {
        _instantGames.Remove(game);
    }
}
