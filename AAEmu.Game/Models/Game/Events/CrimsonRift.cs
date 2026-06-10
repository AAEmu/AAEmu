using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

using NLog;

namespace AAEmu.Game.Models.Game.Events;

/// <summary>
/// Server-side Crimson Rift event driver for quests 2941, 2942, and 2943.
/// The event opens at 13:00 in-game time, advances each rift after its wave is
/// cleared, and force-stops at 16:00.
/// </summary>
public class CrimsonRift : IEvent, IObserver<float>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static readonly CrimsonRift Instance = new();

    public uint Id { get; set; }
    public uint ZoneKey { get; set; }
    public uint MapKey { get; set; }

    public const float TriggerHour = 13f;
    public const float ForceEndHour = 16f;

    public static readonly Dictionary<string, uint> TowerDefIdsByRegion = new()
    {
        ["Ynystere"] = 5,
        ["Cinderstone"] = 3,
        ["Auroria"] = 6,
    };

    public static readonly Dictionary<string, List<WorldSpawnPosition>> SpawnPointsByRegion = new()
    {
        ["Ynystere"] =
        [
            new() { WorldId = 1, ZoneId = 0, X = 21419.9f, Y = 12618.6f, Z = 227.6f },
        ],
        ["Cinderstone"] = [],
    };

    public static readonly List<uint> Phase1Mobs =
    [
        8826, // Crimson Army Infantryman
        8834, // Crimson Army Archer
    ];

    public static readonly List<uint> Phase2Mobs =
    [
        8827, // Crimson Army Soldier
        8835, // Crimson Army Reservist
    ];

    public static readonly List<uint> Phase3Mobs =
    [
        8836, // Crimson Army Spearman
        8825, // Crimson Army Commander
    ];

    public static readonly List<uint> Phase4Mobs =
    [
        8850, // Hound of Kyrios
    ];

    private const int TotalPhases = 3;
    private const int MobsPerPhase = 40;
    private const float SpawnRadius = 25f;

    private sealed class RiftInstance
    {
        public string Region { get; init; }
        public uint TowerDefId { get; init; }
        public WorldSpawnPosition Origin { get; init; }
        public int Phase { get; set; }
        public int Kills { get; set; }
        public readonly HashSet<uint> LiveMobObjIds = [];
        public readonly List<Npc> LiveMobs = [];
    }

    private readonly object _lock = new();
    private bool _running;
    private readonly List<RiftInstance> _instances = [];
    private WorldInstance _world;
    private EventHandler<OnUnitKilledArgs> _killHandler;
    private float _lastObservedHour = -1f;
    private IDisposable _timeSubscription;

    public void Register()
    {
        _timeSubscription ??= TimeManager.Instance.Subscribe(this);
    }

    public void OnNext(float currentHour)
    {
        var previousHour = _lastObservedHour;
        _lastObservedHour = currentHour;

        if (previousHour >= 0f)
        {
            if (CrossedHour(previousHour, currentHour, TriggerHour))
                StartFromSchedule();

            if (_running && CrossedHour(previousHour, currentHour, ForceEndHour))
            {
                Logger.Info("CrimsonRift: force-stop at {0:0.##}h (in-game)", ForceEndHour);
                Stop();
            }
        }
        else if (IsWithinActiveWindow(currentHour))
        {
            Logger.Info("CrimsonRift: registering during active window at {0:0.##}h (in-game)", currentHour);
            StartFromSchedule();
        }

        if (_running)
            Update();
    }

    internal static bool CrossedHour(float previousHour, float currentHour, float targetHour)
    {
        var crossedForward = previousHour < targetHour && currentHour >= targetHour;
        var crossedWrap = previousHour > currentHour && (targetHour > previousHour || targetHour <= currentHour);
        return crossedForward || crossedWrap;
    }

    internal static bool IsWithinActiveWindow(float currentHour)
    {
        return currentHour >= TriggerHour && currentHour < ForceEndHour;
    }

    public void OnError(Exception error)
    {
    }

    public void OnCompleted()
    {
    }

    protected virtual void StartFromSchedule()
    {
        Start();
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_running)
            {
                Logger.Warn("CrimsonRift.Start: already running, ignoring");
                return;
            }

            foreach (var (region, points) in SpawnPointsByRegion)
            {
                if (points.Count == 0)
                {
                    Logger.Warn("CrimsonRift.Start: region '{0}' has no spawn points, skipping", region);
                    continue;
                }

                var origin = points[Random.Shared.Next(points.Count)];
                var towerDefId = TowerDefIdsByRegion.GetValueOrDefault(region);
                _instances.Add(new RiftInstance
                {
                    Region = region,
                    TowerDefId = towerDefId,
                    Origin = origin,
                });
            }

            if (_instances.Count == 0)
            {
                Logger.Warn("CrimsonRift.Start: no spawn points configured in any region");
                return;
            }

            _world = WorldManager.Instance.MainWorld;
            if (_world == null)
            {
                Logger.Warn("CrimsonRift.Start: MainWorld not initialized yet");
                _instances.Clear();
                return;
            }

            _killHandler = OnUnitKilledHandler;
            _world.Events.OnUnitKilled += _killHandler;
            _running = true;

            Register();

            Logger.Info("CrimsonRift: starting with {0} rifts", _instances.Count);
            foreach (var instance in _instances)
            {
                BroadcastStart(instance);
                BroadcastTowerDefMsg(instance, useEndMsg: false);
                BeginPhase(instance, 1);
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running)
                return;

            _running = false;

            if (_world != null && _killHandler != null)
                _world.Events.OnUnitKilled -= _killHandler;

            _killHandler = null;

            foreach (var instance in _instances)
            {
                DespawnMobs(instance);
                BroadcastEnd(instance);
                BroadcastTowerDefMsg(instance, useEndMsg: true);
            }

            Logger.Info("CrimsonRift: stopped");
            _instances.Clear();
        }
    }

    public void Update()
    {
        lock (_lock)
        {
            if (!_running)
                return;

            var allFinished = true;
            foreach (var instance in _instances)
            {
                var phaseCleared = instance.Kills >= MobsPerPhase;
                if (!phaseCleared)
                {
                    allFinished = false;
                    continue;
                }

                if (instance.Phase < TotalPhases)
                {
                    BeginPhase(instance, instance.Phase + 1);
                    allFinished = false;
                }
            }

            if (allFinished)
                Stop();
        }
    }

    private void BeginPhase(RiftInstance instance, int phase)
    {
        DespawnMobs(instance);
        instance.Phase = phase;
        instance.Kills = 0;
        BroadcastPhase(instance, phase);
        BroadcastProgMsg(instance, phase);
        SpawnMobs(instance);
        Logger.Info("CrimsonRift[{0}]: phase {1} started ({2} spawns)",
            instance.Region, phase, instance.LiveMobs.Count);
    }

    private static void BroadcastProgMsg(RiftInstance instance, int phase)
    {
        var towerDef = TowerDefGameData.Instance.GetTowerDef(instance.TowerDefId);
        if (towerDef?.Progs == null || phase < 0 || phase >= towerDef.Progs.Count)
            return;

        var msg = towerDef.Progs[phase]?.Msg;
        if (!string.IsNullOrEmpty(msg))
            Announce(msg);
    }

    private static void BroadcastTowerDefMsg(RiftInstance instance, bool useEndMsg)
    {
        var towerDef = TowerDefGameData.Instance.GetTowerDef(instance.TowerDefId);
        var msg = useEndMsg ? towerDef?.EndMsg : towerDef?.StartMsg;
        if (!string.IsNullOrEmpty(msg))
            Announce(msg);
    }

    private void SpawnMobs(RiftInstance instance)
    {
        var templates = instance.Phase switch
        {
            1 => Phase1Mobs,
            2 => Phase2Mobs,
            3 => Phase3Mobs,
            4 => Phase4Mobs,
            _ => null,
        };

        if (templates == null || templates.Count == 0)
            return;

        var phaseSpawnCount = instance.Phase == 4 ? Math.Min(MobsPerPhase / 4, 10) : MobsPerPhase;

        for (var i = 0; i < phaseSpawnCount; i++)
        {
            var templateId = templates[i % templates.Count];
            var angle = (float)(Random.Shared.NextDouble() * Math.PI * 2.0);
            var distance = (float)(Random.Shared.NextDouble() * SpawnRadius);
            var spawnX = instance.Origin.X + (float)Math.Cos(angle) * distance;
            var spawnY = instance.Origin.Y + (float)Math.Sin(angle) * distance;

            float spawnZ;
            try
            {
                spawnZ = _world.GetHeight(spawnX, spawnY);
                if (spawnZ <= 0f)
                    spawnZ = instance.Origin.Z;
            }
            catch
            {
                spawnZ = instance.Origin.Z;
            }

            var position = new WorldSpawnPosition
            {
                WorldId = instance.Origin.WorldId,
                ZoneId = instance.Origin.ZoneId,
                X = spawnX,
                Y = spawnY,
                Z = spawnZ,
                Yaw = angle,
            };

            var npc = NpcManager.Instance.Create(_world, 0, templateId);
            if (npc == null)
            {
                Logger.Warn("CrimsonRift: NpcManager.Create returned null for template {0}", templateId);
                continue;
            }

            var spawner = new NpcSpawner
            {
                ParentWorld = _world,
                Id = 0,
                UnitId = templateId,
                RespawnTime = 0,
                Position = position,
            };
            _world.SpawnManager.AddNpcSpawner(spawner);

            npc.ParentWorld = _world;
            npc.RegisterNpcEvents();
            npc.Transform.ApplyWorldSpawnPosition(position);

            if (npc.Ai != null)
            {
                npc.Ai.HomePosition = npc.Transform.World.Position;
                npc.Ai.IdlePosition = npc.Ai.HomePosition;
                npc.Ai.GoToSpawn();
            }

            npc.Spawner = spawner;
            npc.Spawn();

            instance.LiveMobs.Add(npc);
            instance.LiveMobObjIds.Add(npc.ObjId);
        }
    }

    private void DespawnMobs(RiftInstance instance)
    {
        foreach (var npc in instance.LiveMobs)
        {
            if (npc == null)
                continue;

            try
            {
                npc.Delete();
            }
            catch (Exception exception)
            {
                Logger.Warn(exception, "CrimsonRift: error deleting npc {0}", npc.ObjId);
            }
        }

        instance.LiveMobs.Clear();
        instance.LiveMobObjIds.Clear();
    }

    private void OnUnitKilledHandler(object sender, OnUnitKilledArgs args)
    {
        if (args.Victim == null)
            return;

        lock (_lock)
        {
            if (!_running)
                return;

            foreach (var instance in _instances)
            {
                if (!instance.LiveMobObjIds.Remove(args.Victim.ObjId))
                    continue;

                instance.Kills++;
                instance.LiveMobs.RemoveAll(npc => npc == null || npc.ObjId == args.Victim.ObjId);
                return;
            }
        }
    }

    private static void Announce(string message)
    {
        WorldManager.Instance.BroadcastPacketToServer(
            new SCNoticeMessagePacket(3, Color.Crimson, 8000, message));
    }

    private static void BroadcastStart(RiftInstance instance)
    {
        var key = new TowerDefKey { TowerDefId = instance.TowerDefId, ZoneGroupId = 5 };
        WorldManager.Instance.BroadcastPacketToServer(new SCTowerDefStartPacket(key, instance.Origin.ZoneId));
    }

    private static void BroadcastPhase(RiftInstance instance, int phase)
    {
        var key = new TowerDefKey { TowerDefId = instance.TowerDefId, ZoneGroupId = 5 };
        WorldManager.Instance.BroadcastPacketToServer(new SCTowerDefWaveStartPacket(key, instance.Origin.ZoneId, (uint)phase));
    }

    private static void BroadcastEnd(RiftInstance instance)
    {
        var key = new TowerDefKey { TowerDefId = instance.TowerDefId, ZoneGroupId = 5 };
        WorldManager.Instance.BroadcastPacketToServer(new SCTowerDefEndPacket(key, instance.Origin.ZoneId));
    }
}
