using System.Collections.Concurrent;
using System.Diagnostics;

using AAEmu.Game.Models.Game.World;
using AAEmu.World.Models;

using NLog;

namespace AAEmu.World.Core.ZoneHost;

public interface IZoneHostProcess
{
    int Id { get; }
    bool HasExited { get; }
    bool WaitForExit(int milliseconds);
    void KillTree();
}

public interface IZoneHostProcessFactory
{
    IZoneHostProcess Start(ZoneHostLaunchSpec spec);
}

/// <summary>
/// Starts and stops <c>AAEmu.ZoneHost.exe</c> processes for dungeon world copies,
/// including an optional warm idle pool claimed on enter.
/// </summary>
public sealed class ZoneHostSupervisor : IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private readonly ZoneHostConfig _config;
    private readonly IZoneHostProcessFactory _factory;
    private readonly Func<string, bool> _fileExists;
    private readonly Action<string> _ensureDirectory;
    private readonly ConcurrentDictionary<uint, TrackedHost> _hosts = new();
    private readonly ConcurrentDictionary<int, byte> _portsInUse = new();
    private readonly object _warmLock = new();
    private readonly Dictionary<string, List<WarmSlot>> _warmByTemplate =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, WarmSlot> _warmByInstanceId = new();
    private int _nextPortOffset;
    private Func<string, WorldInstance> _createWarmWorld;
    private Action<WorldInstance> _disposeWarmWorld;
    private Action<WorldInstance> _onWarmHostReady;
    private Timer _idleCullTimer;

    public ZoneHostSupervisor(
        ZoneHostConfig config,
        IZoneHostProcessFactory factory = null,
        Func<string, bool> fileExists = null,
        Action<string> ensureDirectory = null)
    {
        _config = config ?? new ZoneHostConfig();
        _factory = factory ?? new ProcessZoneHostFactory();
        _fileExists = path => (fileExists ?? File.Exists)(path);
        _ensureDirectory = ensureDirectory ?? (path => Directory.CreateDirectory(path));
    }

    /// <summary>
    /// Supplies WorldInstance create/dispose for warm fill. Required before
    /// <see cref="StartWarmPool"/> / <see cref="EnsureWarm"/>.
    /// <paramref name="onWarmHostReady"/> runs after each idle host starts (content pre-spawn).
    /// </summary>
    public void ConfigureWarmWorldFactory(
        Func<string, WorldInstance> createWorld,
        Action<WorldInstance> disposeWorld,
        Action<WorldInstance> onWarmHostReady = null)
    {
        _createWarmWorld = createWorld ?? throw new ArgumentNullException(nameof(createWorld));
        _disposeWarmWorld = disposeWorld ?? throw new ArgumentNullException(nameof(disposeWorld));
        _onWarmHostReady = onWarmHostReady;
    }

    /// <summary>
    /// Applies env overrides, fills configured templates to Size, and starts idle cull.
    /// </summary>
    public void StartWarmPool(Func<string, string> getEnv = null)
    {
        ZoneHostWarmPool.ApplyEnvironmentOverrides(_config.WarmPool ??= new ZoneHostWarmPoolConfig(), getEnv);
        if (!_config.Enabled || _config.WarmPool is not { Enabled: true })
        {
            Logger.Info("ZoneHost warm pool disabled");
            return;
        }

        if (_createWarmWorld == null || _disposeWarmWorld == null)
        {
            Logger.Error("ZoneHost warm pool enabled but world factory was not configured");
            return;
        }

        EnsureWarm();
        StartIdleCullTimer();
    }

    /// <summary>
    /// Creates idle WorldInstance + ZoneHost slots until each configured template reaches Size.
    /// </summary>
    public void EnsureWarm(string worldTemplateName = null)
    {
        if (!_config.Enabled || _config.WarmPool is not { Enabled: true })
            return;
        if (_createWarmWorld == null)
            return;

        IEnumerable<ZoneHostWarmZoneConfig> zones = _config.WarmPool.Zones;
        if (!string.IsNullOrWhiteSpace(worldTemplateName))
        {
            var one = ZoneHostWarmPool.FindZone(_config.WarmPool, worldTemplateName);
            if (one == null)
                return;
            zones = [one];
        }

        foreach (var zone in zones)
        {
            if (string.IsNullOrWhiteSpace(zone.WorldTemplateName))
                continue;

            var templateName = zone.WorldTemplateName.Trim();
            int target;
            try
            {
                target = ZoneHostWarmPool.ResolveSize(zone.Size, _config.WarmPool.DefaultSize);
            }
            catch (InvalidOperationException ex)
            {
                Logger.Error(ex, "Warm pool size invalid for template {0}", templateName);
                continue;
            }

            while (true)
            {
                int idleCount;
                lock (_warmLock)
                {
                    idleCount = GetSlots(templateName).Count(s => !s.IsBound);
                    if (idleCount >= target)
                        break;
                }

                if (!TrySpawnWarmSlot(templateName))
                    break;
            }
        }
    }

    /// <summary>
    /// Claims an unbound warm slot for <paramref name="ownerId"/> (party id or character id).
    /// On miss, returns false so the cold path can run. Refills only when the pool was fully
    /// culled (no slots left) — not when every slot is already claimed; those refill on
    /// <see cref="StopForWorld"/> so a cold enter does not spawn Size extra idle hosts.
    /// </summary>
    public bool TryClaimWarm(string worldTemplateName, uint ownerId, out WorldInstance world)
    {
        world = null;
        if (!_config.Enabled || _config.WarmPool is not { Enabled: true })
            return false;
        if (string.IsNullOrWhiteSpace(worldTemplateName))
            return false;
        if (!ZoneHostWarmPool.IsTemplateConfigured(_config.WarmPool, worldTemplateName))
            return false;

        var refillAfterCull = false;
        lock (_warmLock)
        {
            var template = worldTemplateName.Trim();
            var slots = GetSlots(template);
            var slot = slots.FirstOrDefault(s => !s.IsBound);
            if (slot == null)
            {
                // All claimed → cold path only; cull wiped every slot → refill idle standby.
                refillAfterCull = slots.Count == 0;
                Logger.Info(
                    refillAfterCull
                        ? "Warm pool culled for {0} — cold spawn + refill"
                        : "Warm pool empty for {0} — cold spawn will be used (refill on release)",
                    worldTemplateName);
            }
            else
            {
                slot.BoundOwnerId = ownerId;
                slot.LastTouchUtc = DateTime.UtcNow;
                world = slot.World;
                Logger.Info(
                    "Warm ZoneHost claimed template={0} world={1} ownerId={2}",
                    worldTemplateName, world.Id, ownerId);
                return true;
            }
        }

        if (refillAfterCull)
        {
            try { EnsureWarm(worldTemplateName); }
            catch (Exception ex) { Logger.Warn(ex, "Warm refill after cull claim miss failed for {0}", worldTemplateName); }
        }

        return false;
    }

    public bool TryStart(WorldInstance world)
    {
        if (world == null)
            return false;
        if (!_config.Enabled)
        {
            Logger.Info("ZoneHost supervisor disabled — dungeon {0} will wait for an already-running host", world.Id);
            return false;
        }

        var zoneName = world.Template?.Name;
        if (string.IsNullOrWhiteSpace(zoneName))
        {
            Logger.Error("ZoneHost start refused — world {0} has no template name", world.Id);
            return false;
        }

        if (!_fileExists(_config.Executable))
        {
            Logger.Error("ZoneHost.Executable was not found: {0}", _config.Executable);
            return false;
        }

        if (!_fileExists(_config.NativeDll))
        {
            Logger.Error("ZoneHost.NativeDll was not found: {0}", _config.NativeDll);
            return false;
        }

        if (!Directory.Exists(_config.WorkingDirectory))
        {
            Logger.Error("ZoneHost.WorkingDirectory was not found: {0}", _config.WorkingDirectory);
            return false;
        }

        if (!_hosts.TryAdd(world.Id, new TrackedHost(null, 0)))
        {
            Logger.Warn("ZoneHost already tracked for world {0}", world.Id);
            return true;
        }

        try
        {
            var svPort = AllocatePort();
            var logRoot = string.IsNullOrWhiteSpace(_config.RuntimeLogRoot)
                ? Path.Combine(Path.GetTempPath(), "AAEmuZoneHost")
                : _config.RuntimeLogRoot;
            var logDirectory = Path.Combine(logRoot, "Logs", $"{Sanitize(zoneName)}-{world.Id}");
            _ensureDirectory(logDirectory);
            var logName = $"ArcheAge-{Sanitize(zoneName)}-{world.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.log";
            var spec = ZoneHostCommandLine.Build(_config, zoneName, world.Id, svPort, logDirectory, logName);
            var process = _factory.Start(spec);
            if (process.WaitForExit(300))
            {
                throw new InvalidOperationException(
                    $"AAEmu.ZoneHost exited immediately for world {world.Id} ({zoneName}). See {logDirectory}");
            }

            _hosts[world.Id] = new TrackedHost(process, svPort);
            Logger.Info(
                "ZoneHost started world={0} zone={1} instanceId={2} svPort={3} pid={4}",
                world.Id, zoneName, world.Id, svPort, process.Id);
            return true;
        }
        catch (Exception ex)
        {
            _hosts.TryRemove(world.Id, out var failed);
            if (failed != null && failed.SvPort != 0)
                _portsInUse.TryRemove(failed.SvPort, out _);
            Logger.Error(ex, "ZoneHost start failed for world {0} ({1})", world.Id, zoneName);
            return false;
        }
    }

    /// <summary>
    /// Stops the ZoneHost for <paramref name="worldId"/> without warm refill.
    /// Prefer <see cref="StopForWorld"/> so warm-listed templates can refill by name.
    /// </summary>
    public void Stop(uint worldId) => StopInternal(worldId, refillWarm: false, disposeWorld: false);

    /// <summary>
    /// Stops idle unbound warm hosts past <see cref="ZoneHostWarmPoolConfig.IdleUnloadSeconds"/>.
    /// Does not refill until a fully-culled claim miss or <see cref="EnsureWarm"/>.
    /// </summary>
    public void CullIdleWarmHosts(DateTime? nowUtc = null)
    {
        if (_config.WarmPool is not { Enabled: true } || _config.WarmPool.IdleUnloadSeconds <= 0)
            return;

        var now = nowUtc ?? DateTime.UtcNow;
        List<WarmSlot> due;
        lock (_warmLock)
        {
            due = _warmByInstanceId.Values
                .Where(s => !s.IsBound && ZoneHostWarmPool.IsIdleDue(s.LastTouchUtc, now, _config.WarmPool.IdleUnloadSeconds))
                .ToList();
        }

        foreach (var slot in due)
        {
            Logger.Info(
                "Warm ZoneHost idle unload template={0} world={1}",
                slot.TemplateName, slot.World.Id);
            StopInternal(slot.World.Id, refillWarm: false, disposeWorld: true);
        }
    }

    public void Dispose()
    {
        _idleCullTimer?.Dispose();
        _idleCullTimer = null;
    }

    private bool TrySpawnWarmSlot(string templateName)
    {
        WorldInstance world = null;
        try
        {
            world = _createWarmWorld(templateName);
            if (world == null)
            {
                Logger.Error("Warm pool create returned null for template {0}", templateName);
                return false;
            }

            if (!TryStart(world))
            {
                _disposeWarmWorld?.Invoke(world);
                return false;
            }

            var slot = new WarmSlot(world, templateName, DateTime.UtcNow);
            lock (_warmLock)
            {
                GetSlots(templateName).Add(slot);
                _warmByInstanceId[world.Id] = slot;
            }

            Logger.Info("Warm ZoneHost idle ready template={0} world={1}", templateName, world.Id);
            try { _onWarmHostReady?.Invoke(world); }
            catch (Exception ex) { Logger.Warn(ex, "Warm host ready callback failed for world {0}", world.Id); }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Warm ZoneHost spawn failed for template {0}", templateName);
            if (world != null)
            {
                try { StopInternal(world.Id, refillWarm: false, disposeWorld: true); }
                catch { /* already logged */ }
            }

            return false;
        }
    }

    private void StopInternal(uint worldId, bool refillWarm, bool disposeWorld)
    {
        string templateName = null;
        WarmSlot removedSlot = null;
        WorldInstance worldToDispose = null;

        lock (_warmLock)
        {
            if (_warmByInstanceId.TryGetValue(worldId, out removedSlot))
            {
                templateName = removedSlot.TemplateName;
                _warmByInstanceId.Remove(worldId);
                if (_warmByTemplate.TryGetValue(templateName, out var list))
                    list.Remove(removedSlot);
                if (disposeWorld)
                    worldToDispose = removedSlot.World;
            }
        }

        if (!_hosts.TryRemove(worldId, out var tracked))
        {
            if (worldToDispose != null)
            {
                try { _disposeWarmWorld?.Invoke(worldToDispose); }
                catch (Exception ex) { Logger.Warn(ex, "Warm world dispose failed for {0}", worldId); }
            }

            MaybeRefill(templateName, refillWarm);
            return;
        }

        try
        {
            if (tracked.Process is { HasExited: false })
                tracked.Process.KillTree();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "ZoneHost stop failed for world {0}", worldId);
        }
        finally
        {
            if (tracked.SvPort != 0)
                _portsInUse.TryRemove(tracked.SvPort, out _);
            Logger.Info("ZoneHost stopped world={0} svPort={1}", worldId, tracked.SvPort);
        }

        if (worldToDispose != null)
        {
            try { _disposeWarmWorld?.Invoke(worldToDispose); }
            catch (Exception ex) { Logger.Warn(ex, "Warm world dispose failed for {0}", worldId); }
        }

        MaybeRefill(templateName, refillWarm);
    }

    private void MaybeRefill(string templateName, bool refillWarm)
    {
        if (!refillWarm || string.IsNullOrWhiteSpace(templateName))
            return;
        if (!ZoneHostWarmPool.IsTemplateConfigured(_config.WarmPool, templateName))
            return;
        try { EnsureWarm(templateName); }
        catch (Exception ex) { Logger.Warn(ex, "Warm refill failed for {0}", templateName); }
    }

    /// <summary>
    /// When stopping a cold-spawned dungeon of a warm-listed template, Dungeon calls Stop
    /// without a warm slot — capture template from the tracked world's name via hook.
    /// </summary>
    public void StopForWorld(WorldInstance world)
    {
        if (world == null)
        {
            return;
        }

        var templateName = world.Template?.Name;
        var wasWarm = false;
        lock (_warmLock)
        {
            wasWarm = _warmByInstanceId.ContainsKey(world.Id);
        }

        StopInternal(world.Id, refillWarm: false, disposeWorld: false);

        if (wasWarm || ZoneHostWarmPool.IsTemplateConfigured(_config.WarmPool, templateName))
            MaybeRefill(templateName, refillWarm: true);
    }

    private List<WarmSlot> GetSlots(string templateName)
    {
        if (!_warmByTemplate.TryGetValue(templateName, out var list))
        {
            list = [];
            _warmByTemplate[templateName] = list;
        }

        return list;
    }

    private void StartIdleCullTimer()
    {
        if (_config.WarmPool.IdleUnloadSeconds <= 0)
            return;

        _idleCullTimer?.Dispose();
        _idleCullTimer = new Timer(
            _ =>
            {
                try { CullIdleWarmHosts(); }
                catch (Exception ex) { Logger.Warn(ex, "Warm idle cull tick failed"); }
            },
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
    }

    private int AllocatePort()
    {
        var basePort = _config.SvPortBase > 0 ? _config.SvPortBase : 65000;
        for (var i = 0; i < 1000; i++)
        {
            var offset = Interlocked.Increment(ref _nextPortOffset);
            var port = basePort + (offset % 1000);
            if (port is < 1 or > 65535)
                continue;
            if (_portsInUse.TryAdd(port, 0))
                return port;
        }

        throw new InvalidOperationException("No free ZoneHost sv_port remained in the configured range.");
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private sealed record TrackedHost(IZoneHostProcess Process, int SvPort);

    private sealed class WarmSlot(WorldInstance world, string templateName, DateTime lastTouchUtc)
    {
        public WorldInstance World { get; } = world;
        public string TemplateName { get; } = templateName;
        public uint? BoundOwnerId { get; set; }
        public DateTime LastTouchUtc { get; set; } = lastTouchUtc;
        public bool IsBound => BoundOwnerId.HasValue;
    }

    private sealed class ProcessZoneHostFactory : IZoneHostProcessFactory
    {
        public IZoneHostProcess Start(ZoneHostLaunchSpec spec)
        {
            // Do not use ProcessStartInfo: CreateNoWindow still inherits World's console, and
            // AllocConsole then fails. DETACHED_PROCESS starts with no console so AllocConsole works.
            var process = ZoneHostWin32.StartDetached(spec);
            return new ProcessHost(process);
        }
    }

    private sealed class ProcessHost(Process process) : IZoneHostProcess
    {
        public int Id
        {
            get
            {
                try { return process.Id; }
                catch { return 0; }
            }
        }

        public bool HasExited
        {
            get
            {
                try { return process.HasExited; }
                catch { return true; }
            }
        }

        public bool WaitForExit(int milliseconds)
        {
            try { return process.WaitForExit(milliseconds); }
            catch { return true; }
        }

        public void KillTree()
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}

