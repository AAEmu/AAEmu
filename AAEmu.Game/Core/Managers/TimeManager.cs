using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// World's read-only view of the day cycle. Zone advances the clock and reports it through
/// ZWTimeOfDay; World uses that same value for time-gated game rules.
/// </summary>
public class TimeManager : Singleton<TimeManager>, ITimeManager
{
    private const float SecondsPerDay = 86400f;
    private float _time = 43200f;
    private float _lastTime = 12f;
    private float _lastRealTime = GetLocalTimeInHours();

    /// <summary>Current game time in hours.</summary>
    public float GetTime => _time / 3600f;

    public float Get()
    {
        return _time;
    }

    /// <summary>Requests every loaded Zone to move its authoritative clock.</summary>
    public void Set(float hours)
    {
        WorldIntegration.RelayTimeOfDayToZones?.Invoke(NormalizeHour(hours));
    }

    /// <summary>Consumes the hour reported by an authoritative Zone.</summary>
    public void OnZoneReport(float hours)
    {
        var time = NormalizeHour(hours);
        var realTime = GetLocalTimeInHours();
        _time = time * 3600f;
        if (_time >= SecondsPerDay)
            _time -= SecondsPerDay;

        OnTimeOfDayChange(time, _lastTime, realTime, _lastRealTime);
        _lastTime = time;
        _lastRealTime = realTime;
    }

    private static float NormalizeHour(float hours)
    {
        var time = hours % 24f;
        return time < 0f ? time + 24f : time;
    }

    private static float GetLocalTimeInHours()
    {
        return (float)DateTime.Now.TimeOfDay.TotalHours;
    }

    private static bool CrossedTime(float oldTime, float newTime, float triggerTime)
    {
        if (oldTime <= newTime)
            return oldTime < triggerTime && triggerTime <= newTime;

        // The selected clock wrapped through midnight.
        return oldTime < triggerTime || triggerTime <= newTime;
    }

    /// <summary>Applies World-owned rules whose gates depend on Zone game time or server wall time.</summary>
    private static void OnTimeOfDayChange(float newTime, float oldTime, float newRealTime, float oldRealTime)
    {
        if ((int)Math.Floor(newTime * 600f) == (int)Math.Floor(oldTime * 600f)
            && (int)Math.Floor(newRealTime * 600f) == (int)Math.Floor(oldRealTime * 600f))
            return;

        foreach (var world in WorldManager.Instance.GetWorlds())
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

                // A phase change replaces this collection, so iterate over a snapshot.
                foreach (var trigger in doodad.CurrentToDTriggers.ToArray())
                {
                    if (trigger.NextPhase <= 0)
                        continue;

                    var triggerOldTime = trigger.IsRealtime ? oldRealTime : oldTime;
                    var triggerNewTime = trigger.IsRealtime ? newRealTime : newTime;
                    if (!CrossedTime(triggerOldTime, triggerNewTime, trigger.TodAsHours))
                        continue;

                    // DoChangePhase executes target phase functions and relays the phase to Zone
                    // before broadcasting the final client state.
                    doodad.DoChangePhase(null, trigger.NextPhase);
                    break;
                }
            }
        }
    }
}
