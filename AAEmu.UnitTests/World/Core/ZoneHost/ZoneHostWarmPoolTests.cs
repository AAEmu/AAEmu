using AAEmu.Game.Models.Game.World;
using AAEmu.World.Core.ZoneHost;
using AAEmu.World.Models;

namespace AAEmu.UnitTests.World.Core.ZoneHost;

public class ZoneHostWarmPoolTests
{
    [Test]
    public async Task ResolveSize_UsesZoneSizeWhenPositive()
    {
        await Assert.That(ZoneHostWarmPool.ResolveSize(3, 2)).IsEqualTo(3);
    }

    [Test]
    public async Task ResolveSize_FallsBackToDefaultSize()
    {
        await Assert.That(ZoneHostWarmPool.ResolveSize(0, 2)).IsEqualTo(2);
    }

    [Test]
    public async Task ResolveSize_InvalidDefault_Throws()
    {
        var threw = false;
        try
        {
            ZoneHostWarmPool.ResolveSize(0, 0);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task ApplyEnvironmentOverrides_ReadsEnvKnobs()
    {
        var config = new ZoneHostWarmPoolConfig
        {
            Enabled = false,
            DefaultSize = 1,
            IdleUnloadSeconds = 60
        };
        ZoneHostWarmPool.ApplyEnvironmentOverrides(config, key => key switch
        {
            ZoneHostWarmPool.EnvEnabled => "1",
            ZoneHostWarmPool.EnvDefaultSize => "4",
            ZoneHostWarmPool.EnvIdleSeconds => "900",
            _ => null
        });

        await Assert.That(config.Enabled).IsTrue();
        await Assert.That(config.DefaultSize).IsEqualTo(4);
        await Assert.That(config.IdleUnloadSeconds).IsEqualTo(900);
    }

    [Test]
    public async Task ApplyEnvironmentOverrides_WarmInstancesKillSwitch_WinsOverEnabledEnv()
    {
        var config = new ZoneHostWarmPoolConfig { Enabled = true };
        ZoneHostWarmPool.ApplyEnvironmentOverrides(config, key => key switch
        {
            ZoneHostWarmPool.EnvWarmInstances => "0",
            ZoneHostWarmPool.EnvEnabled => "1",
            _ => null
        });
        await Assert.That(config.Enabled).IsFalse();

        ZoneHostWarmPool.ApplyEnvironmentOverrides(config, key => key switch
        {
            ZoneHostWarmPool.EnvWarmInstances => "1",
            ZoneHostWarmPool.EnvEnabled => "0",
            _ => null
        });
        await Assert.That(config.Enabled).IsTrue();
    }

    [Test]
    public async Task IsIdleDue_RespectsZeroAndElapsed()
    {
        var touch = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        await Assert.That(ZoneHostWarmPool.IsIdleDue(touch, touch.AddSeconds(10), 0)).IsFalse();
        await Assert.That(ZoneHostWarmPool.IsIdleDue(touch, touch.AddSeconds(10), 30)).IsFalse();
        await Assert.That(ZoneHostWarmPool.IsIdleDue(touch, touch.AddSeconds(30), 30)).IsTrue();
    }

    [Test]
    public async Task IsTemplateConfigured_MatchesName_NotNumericZoneId()
    {
        var config = new ZoneHostWarmPoolConfig
        {
            Enabled = true,
            Zones = [new ZoneHostWarmZoneConfig { WorldTemplateName = "instance_warm_test" }]
        };
        await Assert.That(ZoneHostWarmPool.IsTemplateConfigured(config, "instance_warm_test")).IsTrue();
        await Assert.That(ZoneHostWarmPool.IsTemplateConfigured(config, "INSTANCE_WARM_TEST")).IsTrue();
        await Assert.That(ZoneHostWarmPool.IsTemplateConfigured(config, "265")).IsFalse();
    }
}

public class ZoneHostWarmSupervisorTests
{
    private const string TemplateA = "instance_warm_alpha";
    private const string TemplateB = "instance_warm_beta";

    private sealed class FakeProcess : IZoneHostProcess
    {
        public int Id { get; init; } = 1;
        public bool HasExited { get; set; }
        public bool Killed { get; private set; }
        public bool WaitForExit(int milliseconds) => HasExited;
        public void KillTree() => Killed = true;
    }

    private sealed class FakeFactory : IZoneHostProcessFactory
    {
        public List<ZoneHostLaunchSpec> Specs { get; } = [];
        public List<FakeProcess> Processes { get; } = [];

        public IZoneHostProcess Start(ZoneHostLaunchSpec spec)
        {
            Specs.Add(spec);
            var process = new FakeProcess { Id = Processes.Count + 100 };
            Processes.Add(process);
            return process;
        }
    }

    private static ZoneHostConfig WarmConfig(string templateName, int size = 2, int idleSeconds = 1800) =>
        new()
        {
            Enabled = true,
            Executable = Path.Combine(Path.GetTempPath(), "AAEmu.ZoneHost.exe"),
            WorkingDirectory = Path.GetTempPath(),
            NativeDll = Path.Combine(Path.GetTempPath(), "x2game-dev_dedicate.dll"),
            SvPortBase = 65100,
            WarmPool = new ZoneHostWarmPoolConfig
            {
                Enabled = true,
                DefaultSize = 2,
                IdleUnloadSeconds = idleSeconds,
                Zones = [new ZoneHostWarmZoneConfig { WorldTemplateName = templateName, Size = size }]
            }
        };

    private static (ZoneHostSupervisor Supervisor, FakeFactory Factory, Dictionary<uint, WorldInstance> Worlds, List<uint> ReadyWorldIds)
        CreateWarmSupervisor(ZoneHostConfig config, Action<WorldInstance> onReady = null)
    {
        var factory = new FakeFactory();
        var worlds = new Dictionary<uint, WorldInstance>();
        var readyIds = new List<uint>();
        var nextId = 50u;
        var supervisor = new ZoneHostSupervisor(
            config,
            factory,
            fileExists: _ => true,
            ensureDirectory: _ => { });
        supervisor.ConfigureWarmWorldFactory(
            templateName =>
            {
                var id = nextId++;
                var world = new WorldInstance(new WorldTemplate { Name = templateName }, 0, true, id);
                worlds[id] = world;
                return world;
            },
            world => worlds.Remove(world.Id),
            onWarmHostReady: world =>
            {
                readyIds.Add(world.Id);
                onReady?.Invoke(world);
            });
        return (supervisor, factory, worlds, readyIds);
    }

    [Test]
    public async Task EnsureWarm_FillsIdleSlotsToConfiguredSize()
    {
        var (supervisor, factory, _, readyIds) = CreateWarmSupervisor(WarmConfig(TemplateA, size: 2));
        supervisor.EnsureWarm();

        await Assert.That(factory.Processes.Count).IsEqualTo(2);
        await Assert.That(readyIds.Count).IsEqualTo(2);
        await Assert.That(factory.Specs.All(s => s.Arguments.Contains(TemplateA))).IsTrue();
        // No numeric zone key baked into pool logic.
        await Assert.That(factory.Specs.Any(s => s.Arguments.Contains("265"))).IsFalse();

        supervisor.Dispose();
    }

    [Test]
    public async Task TryClaimWarm_ThenStopForWorld_RefillsIdle()
    {
        var (supervisor, factory, worlds, _) = CreateWarmSupervisor(WarmConfig(TemplateA, size: 2));
        supervisor.EnsureWarm();
        await Assert.That(factory.Processes.Count).IsEqualTo(2);

        await Assert.That(supervisor.TryClaimWarm(TemplateA, ownerId: 42, out var claimed)).IsTrue();
        await Assert.That(claimed).IsNotNull();
        await Assert.That(supervisor.TryClaimWarm(TemplateA, ownerId: 43, out _)).IsTrue();
        var processesWhileFull = factory.Processes.Count;
        // Both claimed: miss must NOT spawn Size idle hosts (cold enter should not double-fill).
        await Assert.That(supervisor.TryClaimWarm(TemplateA, ownerId: 44, out _)).IsFalse();
        await Assert.That(factory.Processes.Count).IsEqualTo(processesWhileFull);

        var claimedWorld = claimed;
        supervisor.StopForWorld(claimedWorld);
        worlds.Remove(claimedWorld.Id);

        // Release should refill one idle slot (Size idle target).
        await Assert.That(factory.Processes.Count).IsGreaterThanOrEqualTo(3);
        await Assert.That(supervisor.TryClaimWarm(TemplateA, ownerId: 45, out var afterRefill)).IsTrue();
        await Assert.That(afterRefill).IsNotNull();

        supervisor.Dispose();
    }

    [Test]
    public async Task TryClaimWarm_AfterCull_RefillsIdleThenMissUsesCold()
    {
        var (supervisor, factory, worlds, _) = CreateWarmSupervisor(WarmConfig(TemplateA, size: 2, idleSeconds: 30));
        supervisor.EnsureWarm();
        await Assert.That(factory.Processes.Count).IsEqualTo(2);

        supervisor.CullIdleWarmHosts(DateTime.UtcNow.AddSeconds(60));
        await Assert.That(worlds.Count).IsEqualTo(0);

        // Fully culled: claim miss refills standby while returning false for cold path.
        await Assert.That(supervisor.TryClaimWarm(TemplateA, ownerId: 99, out _)).IsFalse();
        await Assert.That(factory.Processes.Count).IsEqualTo(4); // 2 culled procs + 2 new idle
        await Assert.That(supervisor.TryClaimWarm(TemplateA, ownerId: 100, out var claimed)).IsTrue();
        await Assert.That(claimed).IsNotNull();

        supervisor.Dispose();
    }

    [Test]
    public async Task CullIdleWarmHosts_StopsUnboundPastIdleSeconds_WithoutImmediateRefill()
    {
        var (supervisor, factory, worlds, _) = CreateWarmSupervisor(WarmConfig(TemplateA, size: 2, idleSeconds: 30));
        supervisor.EnsureWarm();
        await Assert.That(factory.Processes.Count).IsEqualTo(2);

        var now = DateTime.UtcNow.AddSeconds(60);
        supervisor.CullIdleWarmHosts(now);

        await Assert.That(worlds.Count).IsEqualTo(0);
        await Assert.That(factory.Processes.All(p => p.Killed)).IsTrue();
        // Cull must not refill; Fully-culled claim miss / EnsureWarm does.
        await Assert.That(factory.Processes.Count).IsEqualTo(2);

        supervisor.EnsureWarm();
        await Assert.That(factory.Processes.Count).IsEqualTo(4);

        supervisor.Dispose();
    }

    [Test]
    public async Task TryClaimWarm_UnknownTemplate_DoesNotClaim()
    {
        var (supervisor, _, _, _) = CreateWarmSupervisor(WarmConfig(TemplateA, size: 1));
        supervisor.EnsureWarm();
        await Assert.That(supervisor.TryClaimWarm(TemplateB, ownerId: 1, out _)).IsFalse();
        supervisor.Dispose();
    }
}

