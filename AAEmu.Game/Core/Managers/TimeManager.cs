using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Shared in-game day cycle (hours 0–24) for clients, Game-Time schedules, and open-world zone seeds.
/// </summary>
/// <remarks>
/// Seamless open-world dedicades do not report ToD. World owns the shared day math and seeds only
/// zones that belong to the default world template. Other world templates keep a local clock
/// (join seed noon, then zone reports for clients in that instance) and must not rebase this manager.
/// Wall UTC (dailies / Server Time events) is a separate clock.
/// </remarks>
public class TimeManager : Singleton<TimeManager>, ITimeManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Default game-hours per real second (~4 real hours per 24 game hours).
    /// </summary>
    public const float DefaultGameHourSpeed = 0.0016666f;

    /// <summary>
    /// Typical instance start hour (dungeons begin near noon then progress).
    /// Fixed-noon places snap themselves via dedicate speed=0 afterward.
    /// </summary>
    public const float InstanceDefaultStartHour = 12.0f;

    /// <summary>
    /// Max forward game-hours allowed in one tick before world-effect cascades and Game-Time
    /// tower arms are skipped. Shared by <c>TowerDefScheduler</c>.
    /// </summary>
    public const float MaxWorldEffectJumpHours = 0.25f;

    /// <summary>
    /// Shared game-day is owned by the default (open-world) world template.
    /// Unknown or not-yet-loaded zones fail closed (instance-local clock).
    /// </summary>
    public static bool ZoneUsesSharedGameDay(uint zoneId)
    {
        var world = WorldManager.Instance?.GetWorldTemplateByZoneKey(zoneId);
        return UsesSharedGameDay(world, WorldManager.DefaultWorldTemplateId);
    }

    /// <summary>
    /// True only for the default world template. Instance worlds and a missing lookup fail closed.
    /// </summary>
    public static bool UsesSharedGameDay(WorldTemplate world, uint defaultWorldTemplateId)
    {
        if (world == null)
            return false;
        if (world.XmlWorld is { IsInstance: > 0 })
            return false;
        return world.Id == defaultWorldTemplateId;
    }

    /// <summary>True when the forward 24h-circle delta exceeds <see cref="MaxWorldEffectJumpHours"/>.</summary>
    public static bool IsLargeGameHourJump(float oldHours, float newHours) =>
        ForwardHourDelta(oldHours, newHours) > MaxWorldEffectJumpHours;

    private const float SecondsPerDay = 86400f;
    private const double ClientBroadcastSeconds = 10.0;
    private const double ZoneResyncSeconds = 300.0;

    private readonly object _lock = new();
    private bool _started;
    private float _time; // game-day seconds [0, 86400)
    private float _lastTimeHours = float.NaN;
    private float _lastRealTimeHours;
    private DateTime _lastTickUtc = DateTime.UtcNow;
    private DateTime _lastClientBroadcastUtc = DateTime.MinValue;
    private DateTime _lastZonePushUtc = DateTime.MinValue;

    /// <summary>Current game time in hours [0,24).</summary>
    public float GetTime
    {
        get { lock (_lock) return _time / 3600f; }
    }

    public float Get()
    {
        lock (_lock)
            return _time;
    }

    /// <summary>
    /// Arms the shared day clock from wall epoch and starts 1 Hz integration.
    /// Safe to call once after TickManager is running.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_started)
                return;
            _started = true;

            var hours = NormalizeHour(
                (float)((DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds * DefaultGameHourSpeed % 24.0));
            _time = hours * 3600f;
            _lastTimeHours = hours;
            _lastRealTimeHours = GetUtcWallHours();
            _lastTickUtc = DateTime.UtcNow;
            _lastZonePushUtc = DateTime.UtcNow;
        }

        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromSeconds(1), true);
        Logger.Info(
            "Game day clock started — hour={0:F2} speed={1} (~{2:F1} real-h per 24 game-h)",
            GetTime,
            DefaultGameHourSpeed,
            24f / DefaultGameHourSpeed / 3600f);
    }

    /// <summary>Snaps the shared clock (GM /force) and pushes every loaded Zone + clients.</summary>
    public void Set(float hours)
    {
        float oldHours;
        float newHours;
        float oldReal;
        float newReal;
        lock (_lock)
        {
            oldHours = float.IsNaN(_lastTimeHours) ? NormalizeHour(hours) : _lastTimeHours;
            oldReal = _lastRealTimeHours;
            newHours = NormalizeHour(hours);
            _time = newHours * 3600f;
            if (_time >= SecondsPerDay)
                _time -= SecondsPerDay;
            newReal = GetUtcWallHours();
            _lastTimeHours = newHours;
            _lastRealTimeHours = newReal;
            _lastTickUtc = DateTime.UtcNow;
            _lastClientBroadcastUtc = DateTime.UtcNow;
            _lastZonePushUtc = DateTime.UtcNow;
        }

        WorldIntegration.RelayTimeOfDayToZones?.Invoke(newHours);
        BroadcastClients(newHours);
        // Large snaps skip doodad phase walks (crash); Game-Time tower cross still arms.
        OnTimeOfDayChange(newHours, oldHours, newReal, oldReal, allowWorldEffects: true);
        Logger.Info("Game day set {0:F2}h → {1:F2}h (zones+clients pushed)", oldHours, newHours);
    }

    /// <summary>
    /// No-op. Instance / type-2 ZW ToD must not rebase the shared open-world day.
    /// Kept on the interface so older hooks compile; clients get SC from zone-scoped relay only.
    /// </summary>
    public void OnZoneReport(float hours)
    {
        // Intentionally empty — see remarks on class.
    }

    private void Tick(TimeSpan _)
    {
        float oldHours;
        float newHours;
        float oldReal;
        float newReal;
        var pushZones = false;
        var pushClients = false;

        lock (_lock)
        {
            if (!_started)
                return;

            var now = DateTime.UtcNow;
            var dt = (float)(now - _lastTickUtc).TotalSeconds;
            _lastTickUtc = now;
            if (dt <= 0f)
                return;
            if (dt > 30f)
                dt = 1f;

            oldHours = float.IsNaN(_lastTimeHours) ? _time / 3600f : _lastTimeHours;
            oldReal = _lastRealTimeHours;

            var nextHours = NormalizeHour(oldHours + dt * DefaultGameHourSpeed);
            _time = nextHours * 3600f;
            if (_time >= SecondsPerDay)
                _time -= SecondsPerDay;

            newHours = nextHours;
            newReal = GetUtcWallHours();
            _lastTimeHours = newHours;
            _lastRealTimeHours = newReal;

            if ((now - _lastClientBroadcastUtc).TotalSeconds >= ClientBroadcastSeconds)
            {
                _lastClientBroadcastUtc = now;
                pushClients = true;
            }

            if ((now - _lastZonePushUtc).TotalSeconds >= ZoneResyncSeconds)
            {
                _lastZonePushUtc = now;
                pushZones = true;
            }
        }

        OnTimeOfDayChange(newHours, oldHours, newReal, oldReal, allowWorldEffects: true);

        if (pushClients)
            BroadcastClients(newHours);

        if (pushZones)
            WorldIntegration.RelayTimeOfDayToZones?.Invoke(newHours);
    }

    private static void BroadcastClients(float hour)
    {
        // Open-world day only — players inside instances keep their map-local SC from ZW ToD.
        WorldIntegration.ForEachReadyConnection((connection, character) =>
        {
            if (character?.Transform == null || !ZoneUsesSharedGameDay(character.Transform.ZoneId))
                return;
            connection.SendPacket(new SCDetailedTimeOfDayPacket(
                hour, DefaultGameHourSpeed, 0f, 24f));
        });
    }

    private static float NormalizeHour(float hours)
    {
        var time = hours % 24f;
        return time < 0f ? time + 24f : time;
    }

    /// <summary>Forward distance on the 24h circle from old → new.</summary>
    private static float ForwardHourDelta(float oldHours, float newHours)
    {
        var d = NormalizeHour(newHours) - NormalizeHour(oldHours);
        if (d < 0f)
            d += 24f;
        return d;
    }

    private static float GetUtcWallHours()
    {
        return (float)DateTime.UtcNow.TimeOfDay.TotalHours;
    }

    private static bool CrossedTime(float oldTime, float newTime, float triggerTime)
    {
        if (oldTime <= newTime)
            return oldTime < triggerTime && triggerTime <= newTime;

        return oldTime < triggerTime || triggerTime <= newTime;
    }

    private static void OnTimeOfDayChange(
        float newTime,
        float oldTime,
        float newRealTime,
        float oldRealTime,
        bool allowWorldEffects)
    {
        var gameBucketChanged = (int)Math.Floor(newTime * 600f) != (int)Math.Floor(oldTime * 600f);
        var wallBucketChanged = (int)Math.Floor(newRealTime * 600f) != (int)Math.Floor(oldRealTime * 600f);
        if (!gameBucketChanged && !wallBucketChanged)
            return;

        var jump = ForwardHourDelta(oldTime, newTime);
        var largeJump = IsLargeGameHourJump(oldTime, newTime);

        if (!allowWorldEffects)
            return;

        // Large snaps skip doodad ToD cascades. Game-Time arms use a short landing window so a
        // GM hour set does not fire every crossing between the old and new hour.
        if (largeJump)
        {
            if (gameBucketChanged)
            {
                var windowOld = NormalizeHour(newTime - MaxWorldEffectJumpHours + 0.001f);
                Logger.Warn(
                    "ToD jump {0:F2}→{1:F2} (forward Δ={2:F2}h) — skip doodad cascade; " +
                    "Game-Time arms only for landing window {3:F2}→{1:F2}",
                    oldTime, newTime, jump, windowOld);
                WorldIntegration.OnGameTimeAdvanced?.Invoke(windowOld, newTime);
            }
            else
            {
                Logger.Warn(
                    "ToD jump {0:F2}→{1:F2} (forward Δ={2:F2}h) — skip Game-Time tower arms and doodad cascade",
                    oldTime, newTime, jump);
            }

            return;
        }

        if (gameBucketChanged)
            WorldIntegration.OnGameTimeAdvanced?.Invoke(oldTime, newTime);

        var worlds = WorldManager.Instance?.GetWorlds();
        if (worlds == null)
            return;

        foreach (var world in worlds)
        {
            foreach (var npc in world.GetAllNpcs())
            {
                if (npc.Template.NpcPostureSets.Count <= 1)
                    continue;

                var oldAnim =
                    npc.Template.NpcPostureSets.FirstOrDefault(x => x.StartTodTime <= oldTime)?.AnimActionId ?? 0;
                var newAnim =
                    npc.Template.NpcPostureSets.FirstOrDefault(x => x.StartTodTime <= newTime)?.AnimActionId ?? 0;

                if (oldAnim != newAnim)
                    npc.BroadcastPacket(new SCUnitModelPostureChangedPacket(npc, newAnim, true), false);
            }

            foreach (var doodad in world.GetAllDoodads())
            {
                if (doodad.CurrentToDTriggers.Count <= 0)
                    continue;

                foreach (var trigger in doodad.CurrentToDTriggers.ToArray())
                {
                    if (trigger.NextPhase <= 0)
                        continue;

                    var triggerOldTime = trigger.IsRealtime ? oldRealTime : oldTime;
                    var triggerNewTime = trigger.IsRealtime ? newRealTime : newTime;
                    if (!CrossedTime(triggerOldTime, triggerNewTime, trigger.TodAsHours))
                        continue;

                    try
                    {
                        doodad.DoChangePhase(null, trigger.NextPhase);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "ToD phase change failed doodad={0} → phase {1}",
                            doodad.ObjId, trigger.NextPhase);
                    }

                    break;
                }
            }
        }
    }
}
