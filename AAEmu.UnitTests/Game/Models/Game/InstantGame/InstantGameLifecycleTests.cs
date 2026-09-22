using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;

using System.Net;
using System.Net.Sockets;

// The test namespace ends in .InstantGame, so the model type needs the alias.
using InstantGameMatch = AAEmu.Game.Models.Game.InstantGame.InstantGame;

namespace AAEmu.UnitTests.Game.Models.Game.InstantGame;

/// <summary>
/// The full lifecycle: queue → ready → enter → score → finish → leave, the two fill-window
/// expiries, and every per-player exit (leave, disconnect) asserted down to the collections.
/// Everything runs against a fixture battle field whose rule-set values mirror shipped content
/// rows; the heavy world machinery is swapped out through the manager/game seams.
/// </summary>
[NotInParallel]
public class InstantGameLifecycleTests
{
    private const uint BattlefieldId = 99;
    private const uint ZoneKey = 58;
    private const uint WorldInstanceId = 777;

    /// <summary>Mirrors game_score_rules id 53 (rule set 17): kill_enemy_unit_final_hitter, value 0 → 3.</summary>
    private static readonly List<GameScoreRule> KillScoreRules = [new(-1, 200, 0, 3, 0)];

    private sealed class RecordingSession : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => 1;
        public Socket Socket => null!;
        public void SendPacket(byte[] packet) => Packets.Add(packet);
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }

        public ushort[] Opcodes => Packets.Select(p => BitConverter.ToUInt16(p, 6)).ToArray();
        public int CountOf(ushort opcode) => Opcodes.Count(o => o == opcode);
    }

    private sealed class TestEnvironment : IDisposable
    {
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly Dictionary<Character, RecordingSession> _sessions = [];
        private readonly object _previousWorldManager;
        private readonly object _previousSusManager;
        private readonly object _previousZoneManager;
        private readonly object _previousBattlefieldData;
        private readonly object _previousInstantGameManager;
        private int _worldReleases;
        private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public Battlefield Battlefield { get; }
        public InstantGameManager Manager { get; }
        public InstantGameMatch CreatedGame { get; private set; }
        public TaskCompletionSource PlayingGate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int WorldReleases => _worldReleases;

        public TestEnvironment(int corpsSize)
        {
            Battlefield = new Battlefield
            {
                Id = BattlefieldId,
                ZoneKey = ZoneKey,
                InstanceId = 0,
                InstanceUiKindId = 0,
                SquadNotUse = false,
                // Content-shaped timing: instances.apply_waiting_time / matching_cleanup_term.
                ApplyWaitingTimeMs = 60_000,
                MatchingCleanupTermMs = 300_000,
                Spawns = new BattlefieldSpawns
                {
                    BattlefieldId = BattlefieldId,
                    Corps1Spawn = new Point { X = 10, Y = 10, Z = 10 },
                    Corps2Spawn = new Point { X = 20, Y = 20, Z = 20 },
                },
                RuleSet = new GameRuleSet
                {
                    Id = 17,
                    BattlefieldId = BattlefieldId,
                    CorpsSize = corpsSize,
                    Corps1FactionId = 1,
                    Corps2FactionId = 2,
                    TimeReady = 0,
                    TimePlaying = 15,
                    TimeEnding = 1,
                    TimeResurrectionDelay = 5,
                    // game_rule_sets row 17: victory_score 0 (disabled), victory_kill_count 0,
                    // victory_by_score 't'.
                    VictoryScore = 0,
                    VictoryKillCount = 0,
                    VictoryByScore = true,
                    ScoreRules = KillScoreRules,
                },
            };

            var battlefields = new Dictionary<uint, Battlefield> { [BattlefieldId] = Battlefield };
            var battlefieldData = new BattlefieldGameData();
            SetField(battlefieldData, "_battlefields", battlefields);

            Manager = new InstantGameManager
            {
                UtcNow = () => _now,
                CreateGame = battlefield =>
                {
                    var game = new InstantGameMatch(battlefield, WorldInstanceId,
                        () => Interlocked.Increment(ref _worldReleases));
                    // Stage the lifecycle: only the playing clock waits (on the gate the test
                    // releases); ready hold, countdown, start reset, respawn and the ending hold
                    // run immediately, and a cancelled token still cancels them.
                    game.Delay = async (delay, token) =>
                    {
                        if (delay == TimeSpan.FromMinutes(Battlefield.RuleSet.TimePlaying))
                        {
                            await PlayingGate.Task.WaitAsync(token);
                            return;
                        }

                        token.ThrowIfCancellationRequested();
                    };
                    CreatedGame = game;
                    return game;
                },
            };

            var worldManager = new WorldManager(null, null, null, null, null);
            _previousWorldManager = SwapSingleton(worldManager);
            // DisabledSetPosition routes through SusManager, which has no parameterless
            // constructor for the singleton fallback to new up; hand it the test world.
            _previousSusManager = SwapSingleton(new SusManager(worldManager));
            // Zone changes during entry resolve zones through ZoneManager; give it an (empty,
            // unloaded) zone table instead of the DI-only singleton.
            var zoneManager = new ZoneManager(worldManager);
            // Entry and leave both fire a zone change. Resolving both keys as an open zone with
            // no group keeps the change on its early-return branch instead of walking into chat
            // channels, closed-zone kick timers and account access levels a fixture has no data for.
            SetField(zoneManager, "_zones", new Dictionary<uint, Zone>
            {
                [0] = new Zone { ZoneKey = 0, GroupId = 0, Closed = false },
                [ZoneKey] = new Zone { ZoneKey = ZoneKey, GroupId = 0, Closed = false },
            });
            SetField(zoneManager, "_groups", new Dictionary<uint, ZoneGroup>());
            _previousZoneManager = SwapSingleton(zoneManager);
            _previousBattlefieldData = SwapSingleton(battlefieldData);
            _previousInstantGameManager = SwapSingleton(Manager);
        }

        public void Advance(TimeSpan by) => _now += by;

        public Character NewCharacter(uint id, uint seatedCorpsFaction)
        {
            var session = new RecordingSession();
            var character = new Character(new UnitCustomModelParams())
            {
                Id = id,
                ObjId = id,
                Name = $"Player{id}",
                // SetFaction takes the "already established" branch for the seated corps, which is
                // the only branch that does not need a loaded FactionManager.
                Faction = new SystemFaction { Id = (FactionsEnum)seatedCorpsFaction },
            };
            character.Connection = new GameConnection(session) { ActiveChar = character };
            _sessions[character] = session;

            var characters = (System.Collections.Concurrent.ConcurrentDictionary<uint, Character>)typeof(WorldManager)
                .GetField("_characters", InstanceFields)!.GetValue(WorldManager.Instance)!;
            characters[character.ObjId] = character;
            return character;
        }

        public RecordingSession SessionOf(Character character) => _sessions[character];

        public static T Field<T>(object target, string name) =>
            (T)target.GetType().GetField(name, InstanceFields)!.GetValue(target)!;

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, InstanceFields)!.SetValue(target, value);

        private static object SwapSingleton<T>(T replacement) where T : class
        {
            var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
            var previous = field.GetValue(null);
            field.SetValue(null, replacement);
            return previous;
        }

        private static void RestoreSingleton<T>(object previous) where T : class =>
            typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, previous);

        public void Dispose()
        {
            RestoreSingleton<InstantGameManager>(_previousInstantGameManager);
            RestoreSingleton<BattlefieldGameData>(_previousBattlefieldData);
            RestoreSingleton<SusManager>(_previousSusManager);
            RestoreSingleton<ZoneManager>(_previousZoneManager);
            RestoreSingleton<WorldManager>(_previousWorldManager);
        }
    }

    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Timed out waiting for {what}");
            await Task.Delay(10);
        }
    }

    private static bool HasKillSubscription(Character character, object handlerTarget) =>
        character.Events.OnKill.GetInvocationList()
            .Any(subscriber => ReferenceEquals(subscriber.Target, handlerTarget));

    [Test]
    public async Task Queue_Ready_Enter_Score_Finish_Leave_RunsClean()
    {
        using var env = new TestEnvironment(corpsSize: 1);
        // GetCorps seats the first applicant Corps2 and the second Corps1.
        var first = env.NewCharacter(101, seatedCorpsFaction: 2);
        var second = env.NewCharacter(102, seatedCorpsFaction: 1);

        // Queue
        env.Manager.ApplyToBattlefield(BattlefieldId, InstantCorps.Any, first);
        env.Manager.ApplyToBattlefield(BattlefieldId, InstantCorps.Any, second);
        await Assert.That(env.Manager.GetQueueCount(BattlefieldId)).IsEqualTo(2);

        env.Manager.BattlefieldTick(TimeSpan.FromSeconds(15));
        var game = env.CreatedGame;
        await Assert.That(game).IsNotNull();
        await Assert.That(game.Phase).IsEqualTo(InstantGamePhase.Filling);
        await Assert.That(env.Manager.IsTrackedGame(game)).IsTrue();
        await Assert.That(env.Manager.GetQueueCount(BattlefieldId)).IsEqualTo(0);
        await Assert.That(env.SessionOf(first).CountOf(SCOffsets.SCInviteToInstantGamePacket)).IsEqualTo(1);

        // Enter: accept the invite, then confirm the copy load — the two production entry events.
        game.PlayerInviteResponse(first, true, 0ul);
        game.PlayerInviteResponse(second, true, 0ul);
        await Assert.That(first.Transform.InstanceId).IsEqualTo(WorldInstanceId);

        game.OnEnterWorld(first, 0ul);
        game.OnEnterWorld(second, 0ul);
        // Duplicate load confirmation must not seat anyone twice.
        game.OnEnterWorld(first, 0ul);
        await WaitUntil(() => game.Phase == InstantGamePhase.Playing, "the match to start playing");

        var lifecycleOpcodes = env.SessionOf(second).Opcodes
            .Where(opcode => opcode is SCOffsets.SCInstantGameReadyPacket
                or SCOffsets.SCInstantGameCountDownPacket
                or SCOffsets.SCInstantGameStartPacket)
            .ToArray();
        await Assert.That(lifecycleOpcodes.Length).IsEqualTo(3);
        await Assert.That(lifecycleOpcodes[0]).IsEqualTo(SCOffsets.SCInstantGameReadyPacket);
        await Assert.That(lifecycleOpcodes[1]).IsEqualTo(SCOffsets.SCInstantGameCountDownPacket);
        await Assert.That(lifecycleOpcodes[2]).IsEqualTo(SCOffsets.SCInstantGameStartPacket);
        await Assert.That(env.SessionOf(first).CountOf(SCOffsets.SCInstantGameJoinedPacket)).IsEqualTo(1);

        // Score: content rule (event 200, value 0 → 3 points) decides the award, and a
        // victory_score of 0 — every shipped battlefield row — must not end the match on it.
        game.OnKill(first, new OnKillArgs { Killer = first, Victim = second });
        var members = TestEnvironment.Field<Dictionary<Character, InstantGameTeamMember>>(game, "_members");
        await Assert.That(members[first].Score).IsEqualTo(3);
        await Assert.That(members[first].Kills).IsEqualTo((ushort)1);
        await Assert.That(members[second].Deaths).IsEqualTo((ushort)1);
        await Assert.That(game.Phase).IsEqualTo(InstantGamePhase.Playing);

        // Finish: the playing clock runs out; victory_by_score decides from the final tally.
        env.PlayingGate.SetResult();
        await WaitUntil(() => game.Phase == InstantGamePhase.Finished, "the match to finish");

        var endPackets = env.SessionOf(first).Packets
            .Where(packet => BitConverter.ToUInt16(packet, 6) == SCOffsets.SCInstantGameEndPacket)
            .ToList();
        await Assert.That(endPackets.Count).IsEqualTo(1);
        // Encoded layout: 8-byte header, then the body — zi u64 at body 0 (packet 8), ending
        // reason at body 8 (packet 16).
        await Assert.That(endPackets[0][16]).IsEqualTo((byte)BattlefieldEndingReason.TimeoverCompareScore);
        var corps1Result = TestEnvironment.Field<InstantGameTeamResult>(game, "_corps1Result");
        var corps2Result = TestEnvironment.Field<InstantGameTeamResult>(game, "_corps2Result");
        // The killer was seated Corps2 and scored; victory_by_score sends it the win.
        await Assert.That(corps2Result.State).IsEqualTo(VictoryState.Win);
        await Assert.That(corps1Result.State).IsEqualTo(VictoryState.Lose);

        // Leave: every per-player reference the match held is gone.
        foreach (var character in new[] { first, second })
        {
            await Assert.That(character.CurrentInstantGame).IsNull();
            await Assert.That(character.Transform.InstanceId).IsEqualTo(WorldManager.DefaultInstanceId);
            await Assert.That(HasKillSubscription(character, game)).IsFalse();
            await Assert.That(env.SessionOf(character).CountOf(SCOffsets.SCCancelInstantGamePacket) > 0).IsTrue();
        }

        var players = TestEnvironment.Field<List<Character>>(game, "_players");
        var characterCorps = TestEnvironment.Field<Dictionary<Character, InstantCorps>>(game, "_characterCorps");
        await Assert.That(players).IsEmpty();
        await Assert.That(members).IsEmpty();
        await Assert.That(characterCorps).IsEmpty();
        await Assert.That(env.Manager.IsTrackedGame(game)).IsFalse();
        await Assert.That(env.WorldReleases).IsEqualTo(1);

        // Idempotent finish/teardown: repeating either changes nothing.
        await game.EndGame();
        game.AbandonFilling();
        await Assert.That(game.Phase).IsEqualTo(InstantGamePhase.Finished);
        await Assert.That(env.WorldReleases).IsEqualTo(1);
        await Assert.That(env.SessionOf(first).CountOf(SCOffsets.SCInstantGameEndPacket)).IsEqualTo(1);
    }

    [Test]
    public async Task UnderfilledQueue_ExpiresAndReleasesTheApplicant()
    {
        using var env = new TestEnvironment(corpsSize: 2); // needs 4 applicants to form
        var lonely = env.NewCharacter(201, seatedCorpsFaction: 2);

        env.Manager.ApplyToBattlefield(BattlefieldId, InstantCorps.Any, lonely);
        env.Manager.BattlefieldTick(TimeSpan.FromSeconds(15));

        // One applicant can never fill a four-seat match; nothing forms and nobody moves.
        await Assert.That(env.CreatedGame).IsNull();
        await Assert.That(env.Manager.GetQueueCount(BattlefieldId)).IsEqualTo(1);

        // Past instances.apply_waiting_time (60 s for this fixture): released with the
        // queue-clear ack so the client does not sit on a queue that gave up.
        env.Advance(TimeSpan.FromMilliseconds(env.Battlefield.ApplyWaitingTimeMs + 1000));
        env.Manager.BattlefieldTick(TimeSpan.FromSeconds(15));

        await Assert.That(env.Manager.GetQueueCount(BattlefieldId)).IsEqualTo(0);
        await Assert.That(env.Manager.IsQueued(lonely)).IsFalse();

        var cancel = env.SessionOf(lonely).Opcodes.Count(o => o == SCOffsets.SCCancelInstantGamePacket);
        await Assert.That(cancel).IsEqualTo(1);
        var cancelPacket = env.SessionOf(lonely).Packets
            .First(packet => BitConverter.ToUInt16(packet, 6) == SCOffsets.SCCancelInstantGamePacket);
        // body: u16 ErrorMessage = 0, u8 fromHomeland = 1 (clear-queue branch).
        await Assert.That(BitConverter.ToUInt16(cancelPacket, 8)).IsEqualTo((ushort)0);
        await Assert.That(cancelPacket[10]).IsEqualTo((byte)InstantGameWireContract.CancelBranchClearQueue);
        await Assert.That(lonely.CurrentInstantGame).IsNull();
    }

    [Test]
    public async Task UnderfilledMatch_ExpiresAndReleasesEveryoneItWasHolding()
    {
        using var env = new TestEnvironment(corpsSize: 2); // needs 4 applicants to form
        var seated = env.NewCharacter(301, seatedCorpsFaction: 2);
        var invitedOnly = new[]
        {
            env.NewCharacter(302, seatedCorpsFaction: 1),
            env.NewCharacter(303, seatedCorpsFaction: 2),
            env.NewCharacter(304, seatedCorpsFaction: 1),
        };

        foreach (var character in new[] { seated }.Concat(invitedOnly))
            env.Manager.ApplyToBattlefield(BattlefieldId, InstantCorps.Any, character);

        env.Manager.BattlefieldTick(TimeSpan.FromSeconds(15));
        var game = env.CreatedGame;
        await Assert.That(game).IsNotNull();
        await Assert.That(game.Phase).IsEqualTo(InstantGamePhase.Filling);

        // One player takes the invite and enters; the other three never answer.
        game.PlayerInviteResponse(seated, true, 0ul);
        game.OnEnterWorld(seated, 0ul);
        await Assert.That(game.Phase).IsEqualTo(InstantGamePhase.Filling);

        // Past instances.matching_cleanup_term (300 s for this fixture): the match gives up.
        env.Advance(TimeSpan.FromMilliseconds(env.Battlefield.MatchingCleanupTermMs + 1000));
        env.Manager.BattlefieldTick(TimeSpan.FromSeconds(15));

        await WaitUntil(() => game.Phase == InstantGamePhase.Finished, "the unfilled match to be reaped");
        await Assert.That(env.Manager.IsTrackedGame(game)).IsFalse();
        await Assert.That(env.WorldReleases).IsEqualTo(1);

        var players = TestEnvironment.Field<List<Character>>(game, "_players");
        var members = TestEnvironment.Field<Dictionary<Character, InstantGameTeamMember>>(game, "_members");
        await Assert.That(players).IsEmpty();
        await Assert.That(members).IsEmpty();

        // The entered player is back in the main world; everyone's invite UI is cleared.
        await Assert.That(seated.Transform.InstanceId).IsEqualTo(WorldManager.DefaultInstanceId);
        await Assert.That(HasKillSubscription(seated, game)).IsFalse();
        foreach (var character in new[] { seated }.Concat(invitedOnly))
        {
            await Assert.That(character.CurrentInstantGame).IsNull();
            await Assert.That(env.SessionOf(character).CountOf(SCOffsets.SCCancelInstantGamePacket) > 0).IsTrue();
        }
    }

    [Test]
    public async Task DisconnectMidMatch_ReleasesStateWithoutTouchingTheClosingConnection()
    {
        using var env = new TestEnvironment(corpsSize: 1);
        var dropping = env.NewCharacter(401, seatedCorpsFaction: 2);
        var staying = env.NewCharacter(402, seatedCorpsFaction: 1);

        foreach (var character in new[] { dropping, staying })
            env.Manager.ApplyToBattlefield(BattlefieldId, InstantCorps.Any, character);
        env.Manager.BattlefieldTick(TimeSpan.FromSeconds(15));

        var game = env.CreatedGame;
        foreach (var character in new[] { dropping, staying })
        {
            game.PlayerInviteResponse(character, joins: true, 0ul);
            game.OnEnterWorld(character, 0ul);
        }

        await WaitUntil(() => game.Phase == InstantGamePhase.Playing, "the match to start playing");
        game.OnKill(staying, new OnKillArgs { Killer = staying, Victim = dropping });
        var members = TestEnvironment.Field<Dictionary<Character, InstantGameTeamMember>>(game, "_members");
        var droppedMember = members[dropping];

        var packetsBefore = env.SessionOf(dropping).Packets.Count;
        env.Manager.OnCharacterLogout(dropping);

        // Nothing is written to a connection that is going away.
        await Assert.That(env.SessionOf(dropping).Packets.Count).IsEqualTo(packetsBefore);
        await Assert.That(dropping.CurrentInstantGame).IsNull();
        await Assert.That(HasKillSubscription(dropping, game)).IsFalse();
        await Assert.That(members.ContainsKey(dropping)).IsFalse();
        await Assert.That(game.PlayerCount).IsEqualTo(1);

        // The scoreboard line survives (identity + tally are values now), severed from the object:
        // this is what the finish payload and the expedition Started status read.
        await Assert.That(droppedMember.Present).IsFalse();
        await Assert.That(droppedMember.CharacterId).IsEqualTo(401u);
        var corps2Result = TestEnvironment.Field<InstantGameTeamResult>(game, "_corps2Result");
        await Assert.That(corps2Result.Members.Contains(droppedMember)).IsTrue();

        // Finish with the remaining player; then hammer the finish/teardown again.
        env.PlayingGate.SetResult();
        await WaitUntil(() => game.Phase == InstantGamePhase.Finished, "the match to finish");
        await Assert.That(env.SessionOf(staying).CountOf(SCOffsets.SCInstantGameEndPacket)).IsEqualTo(1);
        await Assert.That(staying.CurrentInstantGame).IsNull();
        await Assert.That(HasKillSubscription(staying, game)).IsFalse();
        await Assert.That(env.Manager.IsTrackedGame(game)).IsFalse();

        await game.EndGame();
        game.AbandonFilling();
        await Assert.That(env.WorldReleases).IsEqualTo(1);
        await Assert.That(env.SessionOf(staying).CountOf(SCOffsets.SCInstantGameEndPacket)).IsEqualTo(1);
        var players = TestEnvironment.Field<List<Character>>(game, "_players");
        await Assert.That(players).IsEmpty();
    }

    [Test]
    public async Task LeaveMidMatch_ReleasesStateAndReturnsThePlayerToTheWorld()
    {
        using var env = new TestEnvironment(corpsSize: 1);
        var leaver = env.NewCharacter(501, seatedCorpsFaction: 2);
        var remaining = env.NewCharacter(502, seatedCorpsFaction: 1);

        foreach (var character in new[] { leaver, remaining })
            env.Manager.ApplyToBattlefield(BattlefieldId, InstantCorps.Any, character);
        env.Manager.BattlefieldTick(TimeSpan.FromSeconds(15));

        var game = env.CreatedGame;
        foreach (var character in new[] { leaver, remaining })
        {
            game.PlayerInviteResponse(character, joins: true, 0ul);
            game.OnEnterWorld(character, 0ul);
        }

        await WaitUntil(() => game.Phase == InstantGamePhase.Playing, "the match to start playing");

        game.LeaveInstantGame(leaver);

        await Assert.That(leaver.CurrentInstantGame).IsNull();
        await Assert.That(HasKillSubscription(leaver, game)).IsFalse();
        await Assert.That(leaver.Transform.InstanceId).IsEqualTo(WorldManager.DefaultInstanceId);
        await Assert.That(game.PlayerCount).IsEqualTo(1);
        var members = TestEnvironment.Field<Dictionary<Character, InstantGameTeamMember>>(game, "_members");
        await Assert.That(members.ContainsKey(leaver)).IsFalse();
        // The leave path clears the client's instance UI through the squad/queue ack.
        await Assert.That(env.SessionOf(leaver).CountOf(SCOffsets.SCCancelInstantGamePacket) > 0).IsTrue();

        // The match plays on and still finishes cleanly with the remaining player.
        env.PlayingGate.SetResult();
        await WaitUntil(() => game.Phase == InstantGamePhase.Finished, "the match to finish");
        await Assert.That(remaining.CurrentInstantGame).IsNull();
        await Assert.That(env.Manager.IsTrackedGame(game)).IsFalse();
        await Assert.That(env.WorldReleases).IsEqualTo(1);
    }
}
