using System.Net;
using System.Net.Sockets;
using System.Reflection;

using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Features;
using AAEmu.Game.Models.Game.Schedules;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Weather;
using AAEmu.Game.Scripts.Commands;
using AAEmu.UnitTests.Game.Core.Packets.G2C;

using DayOfWeek = AAEmu.Game.Models.Game.Schedules.DayOfWeek;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// End-to-end wiring of the schedule-driven weather cycle through WorldManager: the boot
/// evaluation, the snow edges that publish to clients, the late-join replay, and the promise that
/// an unconfigured cycle leaves the established snow lever byte-for-byte alone.
/// </summary>
[NotInParallel]
public class WeatherCycleIntegrationTests
{
    private sealed class RecordingSession(uint sessionId) : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => sessionId;
        public Socket Socket => null!;

        public void SendPacket(byte[] packet) => Packets.Add(packet);
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }

    [Test]
    public async Task UnconfiguredCycle_LeavesSnowLeverAndReplayUntouched()
    {
        var (manager, previousWorld, previousWeather) = CreateWiredWorld();

        try
        {
            var onlineSession = new RecordingSession(1);
            manager.TryAddCharacter(CreateCharacter(1, onlineSession));
            manager.InitializeSnowState(FeaturesWithSnow(true));

            manager.ConfigureWeatherCycle(new WeatherConfig(), Lookup(Schedule(1)));

            // No configured phases: the configured snow bit stays exactly as initialized.
            await Assert.That(manager.IsSnowing).IsTrue();
            await Assert.That(onlineSession.Packets).IsEmpty();

            var joiningSession = new RecordingSession(2);
            manager.OnPlayerJoin(CreateCharacter(2, joiningSession));

            await Assert.That(joiningSession.Packets).HasSingleItem();
            await SCSnowingEverywherePacketTests.AssertSnowPacket(joiningSession.Packets[0], true);
        }
        finally
        {
            Restore(previousWorld, previousWeather);
        }
    }

    [Test]
    public async Task SnowCyclePhase_DrivesClientStateBroadcastAndLateJoinReplay()
    {
        var (manager, previousWorld, previousWeather) = CreateWiredWorld();

        try
        {
            var onlineSession = new RecordingSession(1);
            manager.TryAddCharacter(CreateCharacter(1, onlineSession));
            manager.InitializeSnowState(FeaturesWithSnow(false));

            // The content row spans the boot moment, so the boot evaluation opens the snow phase.
            var config = ConfigWithPhase(5, "Snow");
            manager.ConfigureWeatherCycle(config, Lookup(Schedule(5, startDate: DateTime.UtcNow.Date.AddDays(-1), endDate: DateTime.UtcNow.Date.AddDays(1))));

            await Assert.That(manager.IsSnowing).IsTrue();
            await Assert.That(onlineSession.Packets).HasSingleItem();
            await SCSnowingEverywherePacketTests.AssertSnowPacket(onlineSession.Packets[0], true);

            // A player who joins while the phase is open replays the cycle's current state.
            var joinDuringSnowSession = new RecordingSession(2);
            manager.OnPlayerJoin(CreateCharacter(2, joinDuringSnowSession));
            await Assert.That(joinDuringSnowSession.Packets).HasSingleItem();
            await SCSnowingEverywherePacketTests.AssertSnowPacket(joinDuringSnowSession.Packets[0], true);

            // The phase closes (evaluated far past the content bounds): snow turns off for
            // everyone online through the same established packet.
            WeatherManager.Instance.Refresh(new DateTime(2098, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            await Assert.That(manager.IsSnowing).IsFalse();
            await Assert.That(onlineSession.Packets).HasCount().EqualTo(2);
            await SCSnowingEverywherePacketTests.AssertSnowPacket(onlineSession.Packets[1], false);

            var joinAfterSnowSession = new RecordingSession(3);
            manager.OnPlayerJoin(CreateCharacter(3, joinAfterSnowSession));
            await Assert.That(joinAfterSnowSession.Packets).HasSingleItem();
            await SCSnowingEverywherePacketTests.AssertSnowPacket(joinAfterSnowSession.Packets[0], false);
        }
        finally
        {
            Restore(previousWorld, previousWeather);
        }
    }

    [Test]
    public async Task SnowPhaseClosing_KeepsConfiguredSnowOn()
    {
        var (manager, previousWorld, previousWeather) = CreateWiredWorld();

        try
        {
            var onlineSession = new RecordingSession(1);
            manager.TryAddCharacter(CreateCharacter(1, onlineSession));
            manager.InitializeSnowState(FeaturesWithSnow(true));
            manager.ConfigureWeatherCycle(ConfigWithPhase(6, "Snow"), Lookup(FutureSchedule(6)));

            WeatherManager.Instance.Refresh(InsideFutureWindow);
            await Assert.That(WeatherManager.Instance.CurrentState).IsEqualTo(WeatherState.Snow);
            WeatherManager.Instance.Refresh(PastEveryWindow);
            await Assert.That(WeatherManager.Instance.CurrentState).IsEqualTo(WeatherState.Clear);

            // The phase never turned snow on, so closing it must not turn the configured snow off.
            await Assert.That(manager.IsSnowing).IsTrue();
            await Assert.That(onlineSession.Packets).IsEmpty();

            var joiningSession = new RecordingSession(2);
            manager.OnPlayerJoin(CreateCharacter(2, joiningSession));
            await Assert.That(joiningSession.Packets).HasSingleItem();
            await SCSnowingEverywherePacketTests.AssertSnowPacket(joiningSession.Packets[0], true);
        }
        finally
        {
            Restore(previousWorld, previousWeather);
        }
    }

    [Test]
    public async Task SnowPhaseClosing_KeepsOperatorSnowOn()
    {
        var (manager, previousWorld, previousWeather) = CreateWiredWorld();

        try
        {
            var onlineSession = new RecordingSession(1);
            var operatorCharacter = CreateCharacter(1, onlineSession);
            manager.TryAddCharacter(operatorCharacter);
            manager.InitializeSnowState(FeaturesWithSnow(false));
            manager.ConfigureWeatherCycle(ConfigWithPhase(6, "Snow"), Lookup(FutureSchedule(6)));

            new Snow().Execute(operatorCharacter, [bool.TrueString], null!);
            await Assert.That(onlineSession.Packets).HasSingleItem();

            WeatherManager.Instance.Refresh(InsideFutureWindow);
            WeatherManager.Instance.Refresh(PastEveryWindow);

            await Assert.That(manager.IsSnowing).IsTrue();
            await Assert.That(onlineSession.Packets).HasSingleItem();
            await SCSnowingEverywherePacketTests.AssertSnowPacket(onlineSession.Packets[0], true);
        }
        finally
        {
            Restore(previousWorld, previousWeather);
        }
    }

    [Test]
    public async Task OperatorSnowOff_HoldsThroughTheNextSnowPhase()
    {
        var (manager, previousWorld, previousWeather) = CreateWiredWorld();

        try
        {
            var onlineSession = new RecordingSession(1);
            var operatorCharacter = CreateCharacter(1, onlineSession);
            manager.TryAddCharacter(operatorCharacter);
            manager.InitializeSnowState(FeaturesWithSnow(false));
            manager.ConfigureWeatherCycle(ConfigWithPhase(6, "Snow"), Lookup(FutureSchedule(6)));

            WeatherManager.Instance.Refresh(InsideFutureWindow);
            await Assert.That(manager.IsSnowing).IsTrue();
            await Assert.That(onlineSession.Packets).HasSingleItem();

            new Snow().Execute(operatorCharacter, [bool.FalseString], null!);
            await Assert.That(manager.IsSnowing).IsFalse();
            await Assert.That(onlineSession.Packets).HasCount().EqualTo(2);
            await SCSnowingEverywherePacketTests.AssertSnowPacket(onlineSession.Packets[1], false);

            // The phase closes and opens again: the hold stands and nothing more is sent.
            WeatherManager.Instance.Refresh(PastEveryWindow);
            WeatherManager.Instance.Refresh(InsideFutureWindow);
            await Assert.That(WeatherManager.Instance.CurrentState).IsEqualTo(WeatherState.Snow);
            await Assert.That(manager.IsSnowing).IsFalse();
            await Assert.That(onlineSession.Packets).HasCount().EqualTo(2);
        }
        finally
        {
            Restore(previousWorld, previousWeather);
        }
    }

    [Test]
    public async Task SnowAuto_HandsSnowBackToTheCycle()
    {
        var (manager, previousWorld, previousWeather) = CreateWiredWorld();

        try
        {
            var onlineSession = new RecordingSession(1);
            var operatorCharacter = CreateCharacter(1, onlineSession);
            manager.TryAddCharacter(operatorCharacter);
            manager.InitializeSnowState(FeaturesWithSnow(false));
            manager.ConfigureWeatherCycle(ConfigWithPhase(6, "Snow"), Lookup(FutureSchedule(6)));

            WeatherManager.Instance.Refresh(InsideFutureWindow);
            var command = new Snow();
            command.Execute(operatorCharacter, [bool.FalseString], null!);
            command.Execute(operatorCharacter, ["auto"], null!);

            // Released during the open phase: the cycle's snow comes straight back.
            await Assert.That(manager.IsSnowing).IsTrue();
            await Assert.That(onlineSession.Packets).HasCount().EqualTo(3);
            await SCSnowingEverywherePacketTests.AssertSnowPacket(onlineSession.Packets[2], true);

            // And the cycle owns it again, so closing the phase turns it off.
            WeatherManager.Instance.Refresh(PastEveryWindow);
            await Assert.That(manager.IsSnowing).IsFalse();
            await Assert.That(onlineSession.Packets).HasCount().EqualTo(4);
            await SCSnowingEverywherePacketTests.AssertSnowPacket(onlineSession.Packets[3], false);
        }
        finally
        {
            Restore(previousWorld, previousWeather);
        }
    }

    private static readonly DateTime InsideFutureWindow = new(2090, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PastEveryWindow = new(2098, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A snow window that is closed now and open on <see cref="InsideFutureWindow"/>.</summary>
    private static GameSchedules FutureSchedule(int id) =>
        Schedule(id, startDate: new DateTime(2090, 1, 1), endDate: new DateTime(2090, 1, 2));

    private static (WorldManager Manager, object PreviousWorld, object PreviousWeather) CreateWiredWorld()
    {
        var manager = new WorldManager(
            Mock.Of<ITickManager>().Object,
            Mock.Of<IWorldIdManager>().Object,
            new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
            new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
            new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));

        var previousWorld = SwapSingleton(typeof(Singleton<WorldManager>), manager);
        var previousWeather = SwapSingleton(typeof(Singleton<WeatherManager>), new WeatherManager());
        return (manager, previousWorld, previousWeather);
    }

    private static object SwapSingleton(Type singletonType, object instance)
    {
        var field = singletonType.GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        field.SetValue(null, instance);
        return previous;
    }

    private static void Restore(object previousWorld, object previousWeather)
    {
        SwapSingleton(typeof(Singleton<WorldManager>), previousWorld);
        SwapSingleton(typeof(Singleton<WeatherManager>), previousWeather);
    }

    private static Character CreateCharacter(uint id, ISession session)
    {
        var character = new Character(new UnitCustomModelParams()) { Id = id, ObjId = id, Name = $"Player{id}" };
        character.Connection = new GameConnection(session) { ActiveChar = character };
        return character;
    }

    private static FeatureSet FeaturesWithSnow(bool snowing)
    {
        var features = new FeatureSet();
        features.Set(Feature.fset_7_2_unknown, snowing);
        return features;
    }

    private static WeatherConfig ConfigWithPhase(int scheduleId, string state) => new()
    {
        Phases = [new WeatherPhaseConfig { ScheduleId = scheduleId, State = state }],
    };

    private static Func<int, GameSchedules> Lookup(params GameSchedules[] rows) =>
        id => Array.Find(rows, row => row.Id == id);

    private static GameSchedules Schedule(int id, DateTime? startDate = null, DateTime? endDate = null) => new()
    {
        Id = id,
        Name = $"schedule {id}",
        DayOfWeekId = DayOfWeek.Invalid,
        StartTime = 0,
        EndTime = 0,
        StYear = startDate?.Year ?? 0,
        StMonth = startDate?.Month ?? 0,
        StDay = startDate?.Day ?? 0,
        EdYear = endDate?.Year ?? 0,
        EdMonth = endDate?.Month ?? 0,
        EdDay = endDate?.Day ?? 0,
    };
}
