using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.World;

using NLog;

using GameTask = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Cycles marine weather doodads at spawners whose unit template is the technical marker 2768 (technical half / active variants below).
/// </summary>
public sealed class SeaWeatherPointManager : Singleton<SeaWeatherPointManager>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    // Technical half / shared cycle: marker spawner unit, ratio pool for next active template, fallback half length when MinTime unset.
    private const uint MarkerTemplateId = 2768;
    private const uint RatioPoolPhaseGroupId = 7055;
    private const int FallbackHalfMs = 600_000;

    /// <summary>
    /// One active marine-weather doodad variant: same server logic for all rows; only template and phase group ids differ.
    /// </summary>
    private readonly record struct SeaWeatherActiveKind(
        uint TemplateId,
        int LaunchPhaseGroupId,
        int ShutdownPhaseGroupId,
        int ShutdownPhaseDurationMs);

    private static readonly SeaWeatherActiveKind[] ActiveKinds =
    [
        // Rain cloud
        new(TemplateId: 3085, LaunchPhaseGroupId: 7132, ShutdownPhaseGroupId: 7052, ShutdownPhaseDurationMs: 10_000),
        // Whirlpool
        new(TemplateId: 3086, LaunchPhaseGroupId: 7139, ShutdownPhaseGroupId: 7054, ShutdownPhaseDurationMs: 10_000),
    ];

    private readonly ConcurrentDictionary<uint, List<SeaWeatherPointRunner>> _runnersByWorld = new();

    public void Load(WorldInstance world)
    {
        if (world?.SpawnManager == null)
            return;

        var spawners = world.SpawnManager.GetDoodadSpawnersByUnitId(MarkerTemplateId);
        if (spawners.Count == 0)
            return;

        UnregisterWorld(world.Id);

        var list = new List<SeaWeatherPointRunner>(spawners.Count);
        foreach (var spawner in spawners)
            list.Add(new SeaWeatherPointRunner(spawner));

        _runnersByWorld[world.Id] = list;
        foreach (var runner in list)
            runner.StartInitialTechnicalHalf();

        Logger.Info("SeaWeatherPointManager: started {0} point(s) in world {1}", list.Count, world.Id);
    }

    public void UnregisterWorld(uint worldId)
    {
        if (!_runnersByWorld.TryRemove(worldId, out var list))
            return;

        foreach (var runner in list)
            runner.Dispose();
    }

    private static bool TryGetActiveKind(uint templateId, out SeaWeatherActiveKind kind)
    {
        foreach (var k in ActiveKinds)
        {
            if (k.TemplateId == templateId)
            {
                kind = k;
                return true;
            }
        }

        kind = default;
        return false;
    }

    private static int DurationMsForTemplate(uint templateId)
    {
        var template = DoodadManager.Instance.GetTemplate(templateId);
        if (template is { MinTime: > 0 })
            // `min_time` in compact.sqlite3 doodad funcs is stored in milliseconds (e.g., 3_600_000 for 1h, 86_400_000 for 24h).
            return template.MinTime;
        return FallbackHalfMs;
    }

    private static uint PickActiveTemplateId()
    {
        var weights = new List<(uint Id, int W)>();
        foreach (var pf in DoodadManager.Instance.GetPhaseFunc(RatioPoolPhaseGroupId))
        {
            if (pf.FuncType != nameof(DoodadFuncRatioRespawn))
                continue;
            if (DoodadManager.Instance.GetPhaseFuncTemplate(pf.FuncId, pf.FuncType) is not DoodadFuncRatioRespawn rr)
                continue;
            if (!TryGetActiveKind(rr.SpawnDoodadId, out _))
                continue;
            if (rr.Ratio <= 0)
                continue;
            weights.Add((rr.SpawnDoodadId, rr.Ratio));
        }

        if (weights.Count == 0)
            return ActiveKinds[Random.Shared.Next(ActiveKinds.Length)].TemplateId;

        var total = 0;
        foreach (var w in weights)
            total += w.W;

        var roll = Random.Shared.Next(total);
        foreach (var (id, w) in weights)
        {
            roll -= w;
            if (roll < 0)
                return id;
        }

        return weights[^1].Id;
    }

    private sealed class SeaWeatherPointRunner : IDisposable
    {
        private readonly DoodadSpawner _spawner;
        private readonly object _sync = new();
        private volatile bool _disposed;
        private readonly List<GameTask> _pending = [];

        public SeaWeatherPointRunner(DoodadSpawner spawner) => _spawner = spawner;

        public void Dispose()
        {
            _disposed = true;
            lock (_sync)
            {
                foreach (var t in _pending)
                    t.Cancel();
                _pending.Clear();
            }
        }

        public void StartInitialTechnicalHalf()
        {
            lock (_sync)
            {
                _spawner.DespawnAllSpawnedDoodads();
                _spawner.RespawnDoodadTemplateId = 0;
                _spawner.Spawn(0);
            }

            var ms = DurationMsForTemplate(MarkerTemplateId);
            Schedule(new TechnicalHalfEndTask(this), TimeSpan.FromMilliseconds(ms));
        }

        private void Schedule(GameTask task, TimeSpan delay)
        {
            lock (_sync)
            {
                if (_disposed)
                    return;
                TaskManager.Instance.Schedule(task, delay);
                _pending.Add(task);
            }
        }

        private void Unregister(GameTask task)
        {
            lock (_sync)
                _pending.Remove(task);
        }

        private void OnTechnicalHalfEnd()
        {
            if (_disposed)
                return;

            var nextId = PickActiveTemplateId();
            if (!TryGetActiveKind(nextId, out var kind))
            {
                Logger.Warn("SeaWeatherPointManager: unknown active template {0}, using first configured kind", nextId);
                kind = ActiveKinds[0];
            }

            var activeMs = DurationMsForTemplate(kind.TemplateId);

            lock (_sync)
            {
                if (_disposed)
                    return;
                _spawner.DespawnAllSpawnedDoodads();
                _spawner.RespawnDoodadTemplateId = kind.TemplateId;
                var doodad = _spawner.Spawn(0);
                doodad?.DoChangePhase(null, kind.LaunchPhaseGroupId);
            }

            var msUntilShutdownPhase = Math.Max(0, activeMs - kind.ShutdownPhaseDurationMs);
            Schedule(new ForcedShutdownPhaseTask(this, kind.TemplateId, kind.ShutdownPhaseGroupId), TimeSpan.FromMilliseconds(msUntilShutdownPhase));

            Schedule(new ActiveHalfEndTask(this), TimeSpan.FromMilliseconds(activeMs));
        }

        private void TryBeginForcedShutdownPhase(uint expectedActiveTemplateId, int shutdownPhaseGroupId)
        {
            if (_disposed)
                return;

            lock (_sync)
            {
                if (_disposed)
                    return;
                var last = _spawner.Last;
                if (last is { TemplateId: var tid } && tid == expectedActiveTemplateId)
                    last.DoChangePhase(null, shutdownPhaseGroupId);
            }
        }

        private void OnActiveHalfEnd()
        {
            if (_disposed)
                return;

            lock (_sync)
            {
                if (_disposed)
                    return;
                _spawner.DespawnAllSpawnedDoodads();
                _spawner.RespawnDoodadTemplateId = MarkerTemplateId;
                _spawner.Spawn(0);
            }

            var ms = DurationMsForTemplate(MarkerTemplateId);
            Schedule(new TechnicalHalfEndTask(this), TimeSpan.FromMilliseconds(ms));
        }

        private sealed class TechnicalHalfEndTask(SeaWeatherPointRunner runner) : GameTask
        {
            public override void Execute()
            {
                runner.Unregister(this);
                if (runner._disposed)
                    return;
                runner.OnTechnicalHalfEnd();
            }
        }

        private sealed class ForcedShutdownPhaseTask(SeaWeatherPointRunner runner, uint expectedActiveTemplateId, int shutdownPhaseGroupId) : GameTask
        {
            public override void Execute()
            {
                runner.Unregister(this);
                if (runner._disposed)
                    return;
                runner.TryBeginForcedShutdownPhase(expectedActiveTemplateId, shutdownPhaseGroupId);
            }
        }

        private sealed class ActiveHalfEndTask(SeaWeatherPointRunner runner) : GameTask
        {
            public override void Execute()
            {
                runner.Unregister(this);
                if (runner._disposed)
                    return;
                runner.OnActiveHalfEnd();
            }
        }
    }
}
