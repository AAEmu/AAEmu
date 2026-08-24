using AAEmu.Game.Models.Game.World;
using AAEmu.World.Core.ZoneHost;
using AAEmu.World.Models;

namespace AAEmu.UnitTests.World.Core.ZoneHost;

public class ZoneHostCommandLineTests
{
    [Test]
    public async Task Build_IncludesInstanceAndUniquePort()
    {
        var spec = ZoneHostCommandLine.Build(
            new ZoneHostConfig
            {
                Executable = Path.Combine(Path.GetTempPath(), "AAEmu.ZoneHost.exe"),
                WorkingDirectory = Path.GetTempPath(),
                NativeDll = Path.Combine(Path.GetTempPath(), "x2game-dev_dedicate.dll"),
                WorldIp = "127.0.0.1",
                WorldPort = 1240,
                DbLocation = "game/db/game_decrypted.sqlite3",
                DisableRendering = true
            },
            zoneName: "instance_howling_abyss",
            instanceId: 7,
            svPort: 65003,
            logDirectory: Path.Combine(Path.GetTempPath(), "howling-7"),
            logName: "ArcheAge-howling-7.log");

        await Assert.That(spec.Arguments).Contains("+zone");
        await Assert.That(spec.Arguments).Contains("instance_howling_abyss");
        await Assert.That(spec.Arguments).Contains("+instance");
        await Assert.That(spec.Arguments).Contains("7");
        await Assert.That(spec.Arguments).Contains("+sv_port");
        await Assert.That(spec.Arguments).Contains("65003");
        await Assert.That(spec.Environment[ZoneHostCommandLine.DllEnvironment])
            .Contains("x2game-dev_dedicate.dll");
        await Assert.That(spec.Executable.Contains("AAchina", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    [Test]
    public async Task Build_MissingExecutable_Throws()
    {
        var threw = false;
        try
        {
            ZoneHostCommandLine.Build(
                new ZoneHostConfig { Executable = "", WorkingDirectory = "x", NativeDll = "y" },
                "instance_howling_abyss",
                1,
                65000,
                Path.GetTempPath(),
                "x.log");
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }
}

public class ZoneHostSupervisorTests
{
    private sealed class FakeProcess : IZoneHostProcess
    {
        public int Id { get; init; } = 42;
        public bool HasExited { get; set; }
        public bool Killed { get; private set; }
        public bool WaitForExit(int milliseconds) => HasExited;
        public void KillTree() => Killed = true;
    }

    private sealed class FakeFactory : IZoneHostProcessFactory
    {
        public ZoneHostLaunchSpec LastSpec { get; private set; }
        public FakeProcess Process { get; } = new();

        public IZoneHostProcess Start(ZoneHostLaunchSpec spec)
        {
            LastSpec = spec;
            return Process;
        }
    }

    [Test]
    public async Task TryStart_Disabled_DoesNotLaunch()
    {
        var factory = new FakeFactory();
        var supervisor = new ZoneHostSupervisor(new ZoneHostConfig { Enabled = false }, factory);
        var started = supervisor.TryStart(null);
        await Assert.That(started).IsFalse();
        await Assert.That(factory.LastSpec).IsNull();
    }

    [Test]
    public async Task TryStart_MissingExecutable_FailsLoudly()
    {
        var factory = new FakeFactory();
        var config = new ZoneHostConfig
        {
            Enabled = true,
            Executable = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"),
            WorkingDirectory = Path.GetTempPath(),
            NativeDll = Path.Combine(Path.GetTempPath(), "dll")
        };
        var supervisor = new ZoneHostSupervisor(config, factory, fileExists: _ => false);
        var world = new WorldInstance(
            new WorldTemplate { Name = "instance_howling_abyss" },
            0,
            true,
            7);
        await Assert.That(supervisor.TryStart(world)).IsFalse();
        await Assert.That(factory.LastSpec).IsNull();
    }

    [Test]
    public async Task TryStart_TwoWorlds_AssignDistinctPorts()
    {
        var factory = new FakeFactory();
        var config = new ZoneHostConfig
        {
            Enabled = true,
            Executable = Path.Combine(Path.GetTempPath(), "AAEmu.ZoneHost.exe"),
            WorkingDirectory = Path.GetTempPath(),
            NativeDll = Path.Combine(Path.GetTempPath(), "x2game-dev_dedicate.dll"),
            SvPortBase = 65000
        };
        var supervisor = new ZoneHostSupervisor(
            config,
            factory,
            fileExists: _ => true,
            ensureDirectory: _ => { });
        var first = new WorldInstance(new WorldTemplate { Name = "instance_howling_abyss" }, 0, true, 7);
        var second = new WorldInstance(new WorldTemplate { Name = "instance_howling_abyss" }, 0, true, 8);

        await Assert.That(supervisor.TryStart(first)).IsTrue();
        var firstPort = factory.LastSpec.Arguments[factory.LastSpec.Arguments.ToList().IndexOf("+sv_port") + 1];
        await Assert.That(supervisor.TryStart(second)).IsTrue();
        var secondPort = factory.LastSpec.Arguments[factory.LastSpec.Arguments.ToList().IndexOf("+sv_port") + 1];

        await Assert.That(firstPort).IsNotEqualTo(secondPort);
        supervisor.Stop(7);
        supervisor.Stop(8);
        await Assert.That(factory.Process.Killed).IsTrue();
    }

    [Test]
    public async Task TryStart_ProcessExitsImmediately_FailsLoudly()
    {
        var factory = new FakeFactory();
        factory.Process.HasExited = true;
        var config = new ZoneHostConfig
        {
            Enabled = true,
            Executable = Path.Combine(Path.GetTempPath(), "AAEmu.ZoneHost.exe"),
            WorkingDirectory = Path.GetTempPath(),
            NativeDll = Path.Combine(Path.GetTempPath(), "x2game-dev_dedicate.dll")
        };
        var supervisor = new ZoneHostSupervisor(
            config,
            factory,
            fileExists: _ => true,
            ensureDirectory: _ => { });
        var world = new WorldInstance(new WorldTemplate { Name = "instance_howling_abyss" }, 0, true, 9);
        await Assert.That(supervisor.TryStart(world)).IsFalse();
        await Assert.That(factory.LastSpec).IsNotNull();
    }
}

public class ZoneHostWin32Tests
{
    [Test]
    public async Task CreationFlags_DetachFromParentConsole()
    {
        await Assert.That(ZoneHostWin32.CreationFlags & ZoneHostWin32.DetachedProcess)
            .IsEqualTo(ZoneHostWin32.DetachedProcess);
    }

    [Test]
    public async Task BuildCommandLine_QuotesPathsWithSpaces()
    {
        var line = ZoneHostWin32.BuildCommandLine(
            @"C:\Program Files\AAEmu.ZoneHost.exe",
            ["+zone", "instance_howling_abyss"]);
        await Assert.That(line).Contains("\"C:\\Program Files\\AAEmu.ZoneHost.exe\"");
        await Assert.That(line).Contains("+zone instance_howling_abyss");
    }

    [Test]
    public async Task BuildEnvironmentBlock_IncludesOverlayKeys()
    {
        var block = ZoneHostWin32.BuildEnvironmentBlock(
            new Dictionary<string, string> { ["AAEMU_ZONE_DLL"] = "x2game-dev_dedicate.dll" });
        var text = System.Text.Encoding.Unicode.GetString(block);
        await Assert.That(text).Contains("AAEMU_ZONE_DLL=x2game-dev_dedicate.dll");
    }
}
