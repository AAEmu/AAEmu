using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions.Activities;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.InstantGame;

public partial class InstantGame
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// How long the countdown before a battle field opens runs for. The client animates it from its
    /// own artwork, one numeral per second, and that artwork stops at five.
    /// </summary>
    private static readonly TimeSpan CountdownDuration = TimeSpan.FromSeconds(5);

    private readonly List<Character> _players;
    private readonly Dictionary<uint, List<Character>> _corps;
    private readonly Dictionary<Character, InstantCorps> _characterCorps;

    private readonly InstantGameTeamResult _corps1Result;
    private readonly InstantGameTeamResult _corps2Result;
    private readonly Dictionary<Character, InstantGameTeamMember> _members;

    private readonly Battlefield _battlefield;
    private readonly ZoneInstanceId _zoneInstanceId;
    private readonly uint _worldInstanceId;
    private readonly Action _releaseWorld;

    private readonly CancellationTokenSource _endGameTokenSource;
    private int _resultSent;
    private int _finishStarted;
    private int _tornDown;

    /// <summary>How the match ended; set by the victory/time-over derivation before the result goes out.</summary>
    private BattlefieldEndingReason _endingReason = BattlefieldEndingReason.TimeoverDraw;

    /// <summary>
    /// Where the match is in Queue → ready → enter → score → finish → leave. One-directional; see
    /// <see cref="InstantGamePhase"/>.
    /// </summary>
    public InstantGamePhase Phase { get; private set; } = InstantGamePhase.Filling;

    /// <summary>
    /// UTC moment this match was created. Drives the underfilled expiry: a match that never fills
    /// gives up after the battle field's <c>instances.matching_cleanup_term</c>.
    /// </summary>
    internal DateTime FillingSinceUtc { get; set; }

    internal int PlayerCount => _players.Count;

    /// <summary>
    /// The staged waits of the lifecycle (ready hold, countdown, respawn, playing, ending).
    /// Injectable so tests can walk every phase without waiting on wall-clock time.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } =
        (delay, token) => Task.Delay(delay, token);

    public InstantGame(Battlefield battlefield) : this(battlefield, CreateBattlefieldCopy(battlefield))
    {
    }

    private static WorldInstance CreateBattlefieldCopy(Battlefield battlefield)
    {
        var worldTemplate = WorldManager.Instance.GetWorldTemplateByZoneKey(battlefield.ZoneKey);
        return WorldManager.Instance.CreateWorldInstance(worldTemplate, 0);
    }

    private InstantGame(Battlefield battlefield, WorldInstance world)
        : this(battlefield, world.Id, () =>
        {
            // Cleans the instance up and returns the instance Id to the pool
            WorldManager.Instance.RemoveWorld(world.Id);
            world.Dispose();
        })
    {
    }

    /// <summary>Test seam: a match bound to a plain instance id instead of the heavy world machinery.</summary>
    internal InstantGame(Battlefield battlefield, uint worldInstanceId, Action releaseWorld)
    {
        _battlefield = battlefield;
        _players = [];

        _members = new Dictionary<Character, InstantGameTeamMember>();
        _corps1Result = new InstantGameTeamResult(VictoryState.Lose, _battlefield.RuleSet.Corps1FactionId);
        _corps2Result = new InstantGameTeamResult(VictoryState.Lose, _battlefield.RuleSet.Corps2FactionId);

        _corps = new Dictionary<uint, List<Character>>
        {
            {_battlefield.RuleSet.Corps1FactionId, [] },
            {_battlefield.RuleSet.Corps2FactionId, [] }
        };

        _characterCorps = new Dictionary<Character, InstantCorps>();

        _worldInstanceId = worldInstanceId;
        _zoneInstanceId = new ZoneInstanceId(_battlefield.ZoneKey, worldInstanceId);
        _releaseWorld = releaseWorld;
        FillingSinceUtc = ServerCalendar.UtcNow;

        _endGameTokenSource = new CancellationTokenSource();
    }

    public void AddPlayer(Character character, InstantCorps corps)
    {
        if (character == null || Phase != InstantGamePhase.Filling)
            return;

        if (_players.Contains(character))
        {
            // Player already exists in game, remove for correction
            ReleasePlayer(character);
        }
        _players.Add(character);
        var factionId = corps == InstantCorps.Corps1 ? _battlefield.RuleSet.Corps1FactionId : _battlefield.RuleSet.Corps2FactionId;
        _corps[factionId].Add(character);
        _characterCorps.Add(character, corps);

        var maxEntry = (uint)(_battlefield.RuleSet.CorpsSize * 2);
        character.SendPacket(new SCInviteToInstantGamePacket(
            invitationTime: InstantGameWireContract.InviteWindowMs,
            zoneInstanceId: _zoneInstanceId,
            type: _battlefield.Id,
            matchingKey: _worldInstanceId,
            accept: (uint)_players.Count,
            maxEntry: maxEntry));
        character.CurrentInstantGame = this;
    }

    /// <summary>True when the character still holds any slot in this match.</summary>
    internal bool ContainsPlayer(Character character) => character != null && _players.Contains(character);

    /// <summary>
    /// Drops a player who declined or cancelled while the match is still filling. A player already
    /// inside the copy leaves through the full leave path; one still sitting at the dialog is
    /// released silently because the dialog itself is what changed state on the client.
    /// </summary>
    /// <returns>True when the character belonged to this match.</returns>
    internal bool WithdrawPlayer(Character character)
    {
        if (character == null || !_players.Contains(character))
            return false;

        if (character.Transform != null && character.Transform.InstanceId == _worldInstanceId)
            LeaveInstantGame(character);
        else
            ReleasePlayer(character);
        return true;
    }

    /// <summary>
    /// Drops every per-player reference this match holds — roster, corps, member tallies, the kill
    /// subscription and the character's back-reference — without sending or moving anything.
    /// This is the single exit used by leave, withdraw, expiry and disconnect.
    /// </summary>
    /// <returns>True when the character was tracked at all.</returns>
    internal bool ReleasePlayer(Character character)
    {
        if (character == null)
            return false;

        var released = _players.Remove(character);
        foreach (var side in _corps.Values)
            side.Remove(character);
        if (_characterCorps.Remove(character))
            released = true;
        if (_members.Remove(character, out var member))
        {
            // The scoreboard line survives until the result (id + name + tallies are values now);
            // only the live character object is handed back.
            member.Present = false;
            released = true;
        }

        // Unsubscribing a handler that was never added is a no-op, so this is safe on every path.
        character.Events.OnKill -= OnKill;
        if (character.CurrentInstantGame == this)
            character.CurrentInstantGame = null;
        return released;
    }

    public bool IsFull => _players.Count == _battlefield.RuleSet.CorpsSize * 2;

    public uint BattlefieldId => _battlefield.Id;

    public InstantCorps GetCorps()
    {
        if (_battlefield.Id == (uint)InstantGameType.Gladiator)
        {
            if (!_characterCorps.ContainsValue(InstantCorps.Corps1))
                return InstantCorps.Corps1;
            if (!_characterCorps.ContainsValue(InstantCorps.Corps2))
                return InstantCorps.Corps2;
            return InstantCorps.Invalid;
        }

        var a = _characterCorps.Count(o => o.Value == InstantCorps.Corps1);
        var b = _characterCorps.Count(o => o.Value == InstantCorps.Corps2);
        return b > a ? InstantCorps.Corps1 : InstantCorps.Corps2;

    }

    public void PlayerInviteResponse(Character character, bool joins, ulong qualifierId)
    {
        if (character == null || Phase != InstantGamePhase.Filling)
            return;
        if (!_characterCorps.ContainsKey(character))
            return;

        if (!joins)
        {
            // Next room, remove from current game then readd to requeue
            InstantGameManager.Instance.WithdrawFromBattlefield(character);
            InstantGameManager.Instance.ApplyToBattlefield(_battlefield.Id, InstantCorps.Any, character);
            return;
        }

        var corps = _characterCorps[character];
        var spawn = corps == InstantCorps.Corps1 ? _battlefield.Spawns.Corps1Spawn : _battlefield.Spawns.Corps2Spawn;
        MoveCharacterToWorld(character, _battlefield.ZoneKey, spawn.X, spawn.Y, spawn.Z);
    }

    /// <summary>
    /// The client confirmed it loaded the match copy (CSInstanceLoaded). Idempotent per phase: a
    /// duplicate load, a non-invitee or a match that already opened is ignored instead of being
    /// counted twice.
    /// </summary>
    public void OnEnterWorld(Character character, ulong qualifierId)
    {
        if (character == null || Phase != InstantGamePhase.Filling)
            return;
        if (!_characterCorps.TryGetValue(character, out var corps))
            return;
        if (_members.ContainsKey(character))
            return;

        character.SendPacket(new SCInstantGameJoinedPacket(_zoneInstanceId, _battlefield.Id));

        if (corps == InstantCorps.Corps1)
            character.SetFaction((FactionsEnum)_battlefield.RuleSet.Corps1FactionId);
        else
            character.SetFaction((FactionsEnum)_battlefield.RuleSet.Corps2FactionId);

        character.Events.OnKill += OnKill;

        var member = new InstantGameTeamMember
        {
            CharacterId = character.Id,
            CharacterName = character.Name,
            Present = true,
        };
        if (_battlefield.IsExpeditionContent && character.Expedition is { } memberExpedition &&
            memberExpedition.GetMember(character) != null)
            member.ExpeditionId = (uint)memberExpedition.Id;
        _members.Add(character, member);

        var result = corps == InstantCorps.Corps1 ? _corps1Result : _corps2Result;
        result.Members.Add(member);
        member.Corps = result;

        // Entering an instance is what commits a squad member to it (same crossing the Indun
        // enter path takes through SquadManager).
        SquadManager.Instance.NotifyGameEnter(character);

        if (_members.Count == _battlefield.RuleSet.CorpsSize * 2)
            BeginOpening();
    }

    /// <summary>
    /// Walks the match from "everyone is here" to "go", which is what releases the players from the
    /// standby screen they land on when they join. A battle field pauses on a ready screen, counts
    /// down, and only then starts; skipping either step leaves its players stuck watching standby,
    /// because their client refuses to start a battle field that never counted down.
    /// </summary>
    private void BeginOpening()
    {
        if (Phase != InstantGamePhase.Filling)
            return;
        Phase = InstantGamePhase.Opening;

        BroadcastPacket(new SCInstantGameReadyPacket(_zoneInstanceId, _battlefield.Id,
            Helpers.UnixTimeNowInMilli(), BuildReadyRoster()));

        Task.Run(async () =>
        {
            await Delay(TimeSpan.FromSeconds(_battlefield.RuleSet.TimeReady), _endGameTokenSource.Token);
            BroadcastPacket(new SCInstantGameCountDownPacket(_zoneInstanceId, Helpers.UnixTimeNowInMilli()));

            await Delay(CountdownDuration, _endGameTokenSource.Token);
            Start();
        }, _endGameTokenSource.Token);
    }

    private List<InstantGameRosterMember> BuildReadyRoster()
    {
        var worldId = (byte)Math.Min(byte.MaxValue, AppConfiguration.Instance.Id);
        return _characterCorps
            .Select(entry => new InstantGameRosterMember(
                worldId,
                entry.Value == InstantCorps.Corps1
                    ? _battlefield.RuleSet.Corps1FactionId
                    : _battlefield.RuleSet.Corps2FactionId,
                entry.Key.Name))
            .ToList();
    }

    private void Start()
    {
        if (Phase != InstantGamePhase.Opening)
            return;
        Phase = InstantGamePhase.Playing;

        BroadcastPacket(new SCInstantGameStartPacket(_zoneInstanceId, Helpers.UnixTimeNowInMilli(),
            InstantGameWireContract.FirstRound));

        // Reset players on Start
        Task.Run(async () =>
        {
            // The reset grace after Start comes from content (instant_game_start_reset_delay_seconds);
            // no row means no grace, logged loudly. Never a literal.
            var startResetDelay =
                AAEmu.Game.GameData.ContentConfigGameData.Instance.TryGetInt("instant_game_start_reset_delay_seconds", out var resetDelaySeconds) && resetDelaySeconds > 0
                    ? TimeSpan.FromSeconds(resetDelaySeconds)
                    : TimeSpan.Zero;
            if (startResetDelay == TimeSpan.Zero)
                NLog.LogManager.GetCurrentClassLogger()
                    .Warn("content_configs row 'instant_game_start_reset_delay_seconds' is absent: the start reset applies immediately.");
            await Delay(startResetDelay, _endGameTokenSource.Token);
            foreach (var (character, _) in _characterCorps)
            {
                if (character == null)
                {
                    continue;
                }

                // Reset HP and MP
                // Reset HP
                character.Hp = character.MaxHp;
                character.Mp = character.MaxMp;
                character.BroadcastPacket(new SCUnitPointsPacket(character.ObjId, character.Hp, character.Mp), true);
                // Reset Buffs
                character.Buffs.RemoveAllEffects();
                // Reset Cooldowns
                character.ResetAllSkillCooldowns(false);
            }
        }, _endGameTokenSource.Token);
        Task.Run(async () =>
        {
            await Delay(TimeSpan.FromMinutes(_battlefield.RuleSet.TimePlaying), _endGameTokenSource.Token);
            ApplyTimeOver();
            await EndGame();
        }, _endGameTokenSource.Token);
    }

    /// <summary>
    /// The playing clock ran out: decide the match from the final tallies, per this rule set's
    /// <c>victory_by_score</c>. A finish already declared by a victory threshold is left alone.
    /// </summary>
    private void ApplyTimeOver()
    {
        if (_finishStarted != 0)
            return;

        var (reason, corps1State, corps2State) = InstantGameResultRules.DeriveTimeOver(
            _corps1Result.Score, _corps2Result.Score,
            _corps1Result.TotalKills, _corps2Result.TotalKills,
            _battlefield.RuleSet.VictoryByScore);
        _endingReason = reason;
        _corps1Result.State = corps1State;
        _corps2Result.State = corps2State;
    }

    /// <summary>
    /// Finish. Safe to call more than once and from any owner (playing clock, a victory, a test):
    /// only the first call sends the result and runs the teardown, so rewards are recorded once.
    /// </summary>
    public async Task EndGame()
    {
        if (Phase is InstantGamePhase.Ending or InstantGamePhase.Finished)
            return;
        if (Interlocked.Exchange(ref _finishStarted, 1) != 0)
            return;
        Phase = InstantGamePhase.Ending;

        SendResult();
        await Delay(TimeSpan.FromMinutes(_battlefield.RuleSet.TimeEnding), CancellationToken.None);
        DestroyInstantGame();
    }

    private void SendResult()
    {
        if (Interlocked.Exchange(ref _resultSent, 1) != 0)
            return;
        BroadcastPacket(new SCInstantGameEndPacket(_zoneInstanceId, _endingReason,
            _corps1Result,
            _corps2Result));
        if (!_battlefield.CanRecordExpeditionHistory || !ExpeditionActivityServices.TryGet(out var activityService))
            return;
        TryRecordExpeditionResults(activityService, _corps1Result, _corps2Result);
        TryRecordExpeditionResults(activityService, _corps2Result, _corps1Result);
    }

    private void TryRecordExpeditionResults(ExpeditionActivityService activityService,
        InstantGameTeamResult result, InstantGameTeamResult opponent)
    {
        try
        {
            RecordExpeditionResults(activityService, result, opponent);
        }
        catch (Exception exception)
        {
            _log.Error(exception, "Failed to persist one expedition instance team result for battlefield {0}",
                _battlefield.Id);
        }
    }

    private void RecordExpeditionResults(ExpeditionActivityService activityService,
        InstantGameTeamResult result, InstantGameTeamResult opponent)
    {
        var playResult = result.State == VictoryState.Win
            ? ExpeditionInstancePlayResult.Win
            : opponent.State == VictoryState.Win
                ? ExpeditionInstancePlayResult.Lose
                : ExpeditionInstancePlayResult.Draw;
        // The history score is the non-negative score earned by this team. The client wire field is unsigned;
        // a score differential would turn a losing team's negative value into a very large client value.
        var score = checked((uint)Math.Max(0, result.Score));
        foreach (var group in result.Members
                     .Where(member => member.ExpeditionId != 0)
                     .GroupBy(member => member.ExpeditionId))
        {
            var members = group.Select(member => new ExpeditionInstanceHistoryMember(
                0, member.CharacterId,
                member.Present
                    ? ExpeditionInstanceMemberStatus.Finished
                    : ExpeditionInstanceMemberStatus.Started)).ToArray();
            activityService.RecordInstanceResult(group.Key, _battlefield.InstanceRankDetailId, _battlefield.InstanceId,
                score, playResult, members, ServerCalendar.UtcNow);
        }
    }

    private void DestroyInstantGame()
    {
        if (!TryBeginTeardown())
            return;
        FinishTeardown();
    }

    /// <summary>
    /// A fill window that closed (cleanup term hit, or nobody left) without the match ever opening.
    /// Everyone still attached is released through the normal leave path — entered players are
    /// returned to the world, dialog-only players just get their invite cleared — and the copy goes
    /// back. No result is sent for a match that never played.
    /// </summary>
    internal void AbandonFilling()
    {
        if (!TryBeginTeardown())
            return;
        FinishTeardown();
    }

    private bool TryBeginTeardown()
    {
        if (Interlocked.Exchange(ref _tornDown, 1) != 0)
            return false;
        Phase = InstantGamePhase.Finished;
        foreach (var character in _players.ToList())
            LeaveInstantGame(character);
        return true;
    }

    private void FinishTeardown()
    {
        // Cancel only: pending staged waits observe the cancellation and unwind. Disposing the
        // source here would make a straggler respawn task's token registration throw instead.
        _endGameTokenSource.Cancel();
        _releaseWorld();
        InstantGameManager.Instance.RemoveGame(this);
    }

    /// <summary>
    /// One player's exit — leave packet, match end, disconnect or fill-window expiry. Restores
    /// faction and squad only for a player who actually entered, hands the copy's location back to
    /// anyone standing in it, and clears the invite/queue UI of anyone who never got in.
    /// </summary>
    public void LeaveInstantGame(Character character)
    {
        if (character == null)
            return;

        var enteredMatch = _members.ContainsKey(character);
        var insideCopy = character.Transform != null && character.Transform.InstanceId == _worldInstanceId;

        ReleasePlayer(character);

        if (enteredMatch)
        {
            character.SetFaction(character.OriginFaction.Id);
            SquadManager.Instance.NotifyGameLeave(character);
        }

        if (!insideCopy)
        {
            // Never made it into the copy: the dialog / queue screen they are still sitting on has
            // to go, or the client waits on a match that no longer exists.
            character.SendPacket(SCCancelInstantGamePacket.ClearQueue());
            return;
        }

        character.DisabledSetPosition = true;

        if (character.MainWorldPosition == null)
        {
            _log.Warn($"Character {character.Name} ({character.Id}) does not have MainWorldPosition when leaving instant game!");
            return;
        }

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

    private void MoveCharacterToWorld(Character character, uint zoneId, float x, float y, float z)
    {
        character.DisabledSetPosition = true;
        character.MainWorldPosition ??= character.Transform.CloneDetached(character);
        character.Transform.ApplyWorldSpawnPosition(
            new WorldSpawnPosition { ZoneId = zoneId, X = x, Y = y, Z = z }, _worldInstanceId);
        character.SendPacket(new SCLoadInstancePacket(_worldInstanceId, zoneId, x, y, z, 0, 0, 0));
    }

    public void BroadcastPacket(GamePacket packet)
    {
        foreach (var player in _players)
        {
            player.SendPacket(packet);
        }
    }
}
