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
    private readonly Dictionary<Character, uint> _expeditionAtEntry;

    private readonly WorldInstance _world;
    private readonly Battlefield _battlefield;
    private readonly ZoneInstanceId _zoneInstanceId;

    private readonly CancellationTokenSource _endGameTokenSource;
    private int _resultSent;

    public InstantGame(Battlefield battlefield)
    {
        _battlefield = battlefield;
        _players = [];

        _members = new Dictionary<Character, InstantGameTeamMember>();
        _expeditionAtEntry = new Dictionary<Character, uint>();
        _corps1Result = new InstantGameTeamResult(VictoryState.Lose, _battlefield.RuleSet.Corps1FactionId);
        _corps2Result = new InstantGameTeamResult(VictoryState.Lose, _battlefield.RuleSet.Corps2FactionId);

        _corps = new Dictionary<uint, List<Character>>
        {
            {_battlefield.RuleSet.Corps1FactionId, [] },
            {_battlefield.RuleSet.Corps2FactionId, [] }
        };

        _characterCorps = new Dictionary<Character, InstantCorps>();

        var worldTemplate = WorldManager.Instance.GetWorldTemplateByZoneKey(_battlefield.ZoneKey);
        _world = WorldManager.Instance.CreateWorldInstance(worldTemplate, 0);
        _zoneInstanceId = new ZoneInstanceId(_battlefield.ZoneKey, _world.Id);

        _endGameTokenSource = new CancellationTokenSource();
    }

    public void AddPlayer(Character character, InstantCorps corps)
    {
        if (_players.Contains(character))
        {
            // Player already exists in game, remove for correction
            RemovePlayer(character);
        }
        _players.Add(character);
        var factionId = corps == InstantCorps.Corps1 ? _battlefield.RuleSet.Corps1FactionId : _battlefield.RuleSet.Corps2FactionId;
        _corps[factionId].Add(character);
        _characterCorps.Add(character, corps);

        var maxEntry = (uint)(_battlefield.RuleSet.CorpsSize * 2);
        character.SendPacket(new SCInviteToInstantGamePacket(
            invitationTime: 300000,
            zoneInstanceId: _zoneInstanceId,
            type: _battlefield.Id,
            matchingKey: _world.Id,
            accept: (uint)_players.Count,
            maxEntry: maxEntry));
        character.CurrentInstantGame = this;
    }

    public bool RemovePlayer(Character character)
    {
        if (character == null)
            return false;

        if (!_players.Contains(character))
            return false;

        _players.Remove(character);

        if (_corps.TryGetValue((uint)InstantCorps.Corps1, out var charsInCorps1))
            charsInCorps1.Remove(character);

        if (_corps.TryGetValue((uint)InstantCorps.Corps2, out var charsInCorps2))
            charsInCorps2.Remove(character);

        _characterCorps.Remove(character);
        character.CurrentInstantGame = null;
        return true;
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

    public void OnEnterWorld(Character character, ulong qualifierId)
    {
        var corps = _characterCorps[character];
        if (_battlefield.IsExpeditionContent && character.Expedition is { } expedition &&
            expedition.GetMember(character) != null)
            _expeditionAtEntry[character] = (uint)expedition.Id;
        character.SendPacket(new SCInstantGameJoinedPacket(_zoneInstanceId, _battlefield.Id));

        if (corps == InstantCorps.Corps1)
            character.SetFaction((FactionsEnum)_battlefield.RuleSet.Corps1FactionId);
        else
            character.SetFaction((FactionsEnum)_battlefield.RuleSet.Corps2FactionId);

        character.Events.OnKill += OnKill;

        var member = new InstantGameTeamMember { Character = character };
        _members.Add(character, member);

        var result = corps == InstantCorps.Corps1 ? _corps1Result : _corps2Result;
        result.Members.Add(member);
        member.Corps = result;

        // TODO: This can be done better.
        // TODO: Game expire after 60 seconds if not enough players
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
        BroadcastPacket(new SCInstantGameReadyPacket(_zoneInstanceId, _battlefield.Id,
            Helpers.UnixTimeNowInMilli(), BuildReadyRoster()));

        Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(_battlefield.RuleSet.TimeReady), _endGameTokenSource.Token);
            BroadcastPacket(new SCInstantGameCountDownPacket(_zoneInstanceId, Helpers.UnixTimeNowInMilli()));

            await Task.Delay(CountdownDuration, _endGameTokenSource.Token);
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
        BroadcastPacket(new SCInstantGameStartPacket(_zoneInstanceId, Helpers.UnixTimeNowInMilli(),
            InstantGameWireContract.FirstRound));

        // Reset players on Start
        Task.Run(async () =>
        {
            await Task.Delay(3000);
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
        });
        Task.Run(async () =>
        {
            await Task.Delay(_battlefield.RuleSet.TimePlaying * 60 * 1000, _endGameTokenSource.Token);
            await EndGame();
        }, _endGameTokenSource.Token);
    }

    public async Task EndGame()
    {
        SendResult();
        await Task.Delay(_battlefield.RuleSet.TimeEnding * 60 * 1000);
        DestroyInstantGame();
    }

    private void SendResult()
    {
        if (Interlocked.Exchange(ref _resultSent, 1) != 0)
            return;
        BroadcastPacket(new SCInstantGameEndPacket(_zoneInstanceId, BattlefieldEndingReason.AchievementScore,
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
                     .Where(member => _expeditionAtEntry.ContainsKey(member.Character))
                     .GroupBy(member => _expeditionAtEntry[member.Character]))
        {
            var members = group.Select(member => new ExpeditionInstanceHistoryMember(
                0, member.Character.Id,
                _players.Contains(member.Character)
                    ? ExpeditionInstanceMemberStatus.Finished
                    : ExpeditionInstanceMemberStatus.Started)).ToArray();
            activityService.RecordInstanceResult(group.Key, _battlefield.InstanceRankDetailId, _battlefield.InstanceId,
                score, playResult, members, ServerCalendar.UtcNow);
        }
    }

    private void DestroyInstantGame()
    {
        foreach (var character in _players.ToList())
        {
            LeaveInstantGame(character);
        }

        // TODO: Unbind all events from characters
        WorldManager.Instance.RemoveWorld(_world.Id);
        // Cleans the instance up and returns the instance Id to the pool
        _world.Dispose();
        InstantGameManager.Instance.RemoveGame(this);
    }

    public void LeaveInstantGame(Character character)
    {
        // Warning: Null exception exists if player does not exist in the world when this is ran (Most likely from disconnecting or character select)

        RemovePlayer(character);
        character.SetFaction(character.OriginFaction.Id);
        character.Events.OnKill -= OnKill;
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
        character.Transform.ApplyWorldSpawnPosition(new WorldSpawnPosition { ZoneId = zoneId, X = x, Y = y, Z = z }, _world.Id);
        character.SendPacket(new SCLoadInstancePacket(_world.Id, zoneId, x, y, z, 0, 0, 0));
    }

    public void BroadcastPacket(GamePacket packet)
    {
        foreach (var player in _players)
        {
            player.SendPacket(packet);
        }
    }
}
