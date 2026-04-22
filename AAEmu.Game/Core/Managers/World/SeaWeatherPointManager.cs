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
/// Cycles marine weather doodads at spawners whose unit template is the technical marker 2768 (technical half / active 3085 or 3086).
/// </summary>
public sealed class SeaWeatherPointManager : Singleton<SeaWeatherPointManager>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const uint MarkerTemplateId = 2768;
    private const uint RatioPoolPhaseGroupId = 7055;
    private const uint RainTemplateId = 3085;
    private const uint WhirlpoolTemplateId = 3086;
    private const int RainLaunchPhase = 7132;
    private const int WhirlpoolLaunchPhase = 7139;
    private const int WhirlpoolShutdownPhase = 7054;
    private const int DefaultHalfMs = 600_000;
    private const int WhirlpoolShutdownLeadMs = 10_000;

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

    private static int DurationMsForTemplate(uint templateId)
    {
        var template = DoodadManager.Instance.GetTemplate(templateId);
        if (template is { MinTime: > 0 })
            return template.MinTime;
        return DefaultHalfMs;
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
            if (rr.SpawnDoodadId is not (RainTemplateId or WhirlpoolTemplateId))
                continue;
            if (rr.Ratio <= 0)
                continue;
            weights.Add((rr.SpawnDoodadId, rr.Ratio));
        }

        if (weights.Count == 0)
            return Random.Shared.Next(2) == 0 ? RainTemplateId : WhirlpoolTemplateId;

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
            var activeMs = DurationMsForTemplate(nextId);
            var launchPhase = nextId == RainTemplateId ? RainLaunchPhase : WhirlpoolLaunchPhase;

            lock (_sync)
            {
                if (_disposed)
                    return;
                if (_spawner.Last != null)
                    _spawner.Despawn(_spawner.Last);
                _spawner.RespawnDoodadTemplateId = nextId;
                var doodad = _spawner.Spawn(0);
                doodad?.DoChangePhase(null, launchPhase);
            }

            if (nextId == WhirlpoolTemplateId)
            {
                var lead = Math.Max(0, activeMs - WhirlpoolShutdownLeadMs);
                Schedule(new WhirlpoolShutdownTask(this), TimeSpan.FromMilliseconds(lead));
            }

            Schedule(new ActiveHalfEndTask(this), TimeSpan.FromMilliseconds(activeMs));
        }

        private void OnWhirlpoolShutdownPhase()
        {
            if (_disposed)
                return;

            lock (_sync)
            {
                if (_disposed)
                    return;
                if (_spawner.Last is { TemplateId: WhirlpoolTemplateId })
                    _spawner.Last.DoChangePhase(null, WhirlpoolShutdownPhase);
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
                if (_spawner.Last != null)
                    _spawner.Despawn(_spawner.Last);
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

        private sealed class WhirlpoolShutdownTask(SeaWeatherPointRunner runner) : GameTask
        {
            public override void Execute()
            {
                runner.Unregister(this);
                if (runner._disposed)
                    return;
                runner.OnWhirlpoolShutdownPhase();
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
