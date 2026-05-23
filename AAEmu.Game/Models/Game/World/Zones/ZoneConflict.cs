using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Tasks.Zones;

using NLog;

namespace AAEmu.Game.Models.Game.World.Zones;

public class ZoneConflict(ZoneGroup owner)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    // ReSharper disable once NotAccessedField.Local
    private ZoneGroup _owner = owner;
    public ushort ZoneGroupId { get; set; }
    public int[] NumKills { get; } = new int[5];
    public int[] NoKillMin { get; } = new int[5];

    public int ConflictMin { get; set; }
    public int WarMin { get; set; }
    public int PeaceMin { get; set; }

    public uint PeaceProtectedFactionId { get; set; }
    public uint NuiaReturnPointId { get; set; }
    public uint HariharaReturnPointId { get; set; }
    public uint WarTowerDefId { get; set; }
    public bool Closed { get; set; } = false;

    public ZoneConflictType CurrentZoneState { get; protected set; } = ZoneConflictType.Tension;
    public DateTime NextStateTime { get; protected set; } = DateTime.MinValue;
    public uint KillCount { get; protected set; }

    /// <summary>
    /// Call this function if a PvP kill happens in a zone
    /// </summary>
    public void AddZoneKill(uint NumberOfKills = 1)
    {
        // Ignore when in conflict, war or peace
        if (CurrentZoneState >= ZoneConflictType.Conflict)
            return;

        // Ignore if this zone doesn't have a kill counter mechanic
        if (NumKills[0] == 0 && NumKills[1] == 0 && NumKills[2] == 0 && NumKills[3] == 0 && NumKills[4] == 0)
            return;

        var LastState = CurrentZoneState;
        KillCount += NumberOfKills;

        if (CurrentZoneState == ZoneConflictType.Tension && KillCount > NumKills[0])
        {
            CurrentZoneState = ZoneConflictType.Danger;
            NextStateTime = DateTime.MinValue;
        }
        if (CurrentZoneState == ZoneConflictType.Danger && KillCount > NumKills[1])
        {
            CurrentZoneState = ZoneConflictType.Dispute;
            NextStateTime = DateTime.MinValue;
        }
        if (CurrentZoneState == ZoneConflictType.Dispute && KillCount > NumKills[2])
        {
            CurrentZoneState = ZoneConflictType.Unrest;
            NextStateTime = DateTime.MinValue;
        }
        if (CurrentZoneState == ZoneConflictType.Unrest && KillCount > NumKills[3])
        {
            CurrentZoneState = ZoneConflictType.Crisis;
            NextStateTime = DateTime.MinValue;
        }
        if (CurrentZoneState == ZoneConflictType.Crisis && KillCount > NumKills[4])
        {
            CurrentZoneState = ZoneConflictType.Conflict;
            NextStateTime = DateTime.UtcNow.AddMinutes(ConflictMin);
            KillCount = 0;
        }
        if (LastState != CurrentZoneState)
        {
            SendSwitchZoneState();
        }
    }

    public void SetTimerTask()
    {
        if (NextStateTime > DateTime.MinValue)
        {
            var lpConflictStartTask = new ZoneStateChangeTask(this);
            var delay = NextStateTime - DateTime.UtcNow;
            Logger.Debug($"ZoneGroup {ZoneGroupId}: scheduling next state check in {delay.TotalMinutes:F1} min (NextStateTime={NextStateTime:HH:mm:ss})");
            TaskManager.Instance.Schedule(lpConflictStartTask, delay);
        }
        else
        {
            Logger.Debug($"ZoneGroup {ZoneGroupId}: no NextStateTime set — timer chain stopped.");
        }
    }

    public void SendSwitchZoneState()
    {
        // Schedule the next timer FIRST, before broadcasting to clients.
        // This guarantees the timer chain is preserved even if BroadcastPacketToServer
        // throws (e.g. transient connection issue, packet encode error).
        SetTimerTask();

        try
        {
            WorldManager.Instance.BroadcastPacketToServer(new SCConflictZoneStatePacket(ZoneGroupId, CurrentZoneState, NextStateTime));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"SendSwitchZoneState: Failed to broadcast zone state for ZoneGroup {ZoneGroupId}, State={CurrentZoneState}");
        }
    }

    public void CheckTimer()
    {
        if (NextStateTime > DateTime.MinValue && DateTime.UtcNow >= NextStateTime)
        {
            Logger.Debug($"ZoneGroup {ZoneGroupId}: timer elapsed, current state={CurrentZoneState}, advancing...");
            ForceNextState();
        }
    }

    public void SetState(ZoneConflictType ct)
    {
        if (ct == CurrentZoneState)
            return;

        var previousState = CurrentZoneState;

        switch (ct)
        {
            case ZoneConflictType.Conflict:
                KillCount = 0;
                NextStateTime = DateTime.UtcNow.AddMinutes(ConflictMin);
                break;
            case ZoneConflictType.War:
                KillCount = 0;
                NextStateTime = DateTime.UtcNow.AddMinutes(WarMin);
                break;
            case ZoneConflictType.Peace:
                KillCount = 0;
                NextStateTime = DateTime.UtcNow.AddMinutes(PeaceMin);
                break;
            default:
                NextStateTime = DateTime.MinValue;
                break;
        }
        CurrentZoneState = ct;
        Logger.Info($"ZoneGroup {ZoneGroupId} changed from {previousState} → {ct} (next state at {NextStateTime:HH:mm:ss})");
        SendSwitchZoneState();
    }

    public void ForceNextState()
    {
        if (CurrentZoneState < ZoneConflictType.Peace)
        {
            if (CurrentZoneState == ZoneConflictType.War && PeaceMin <= 0)
            {
                SetState(ZoneConflictType.Conflict);
            }
            else
            {
                SetState(CurrentZoneState + 1);
            }
        }
        else
        if (CurrentZoneState >= ZoneConflictType.Peace)
        {
            // If it doesn't have a killcounter, go directly back to conflict (ocean areas)
            if (NumKills[0] == 0 && NumKills[1] == 0 && NumKills[2] == 0 && NumKills[3] == 0 && NumKills[4] == 0)
                SetState(ZoneConflictType.Conflict);
            else
                SetState(ZoneConflictType.Tension);
        }
    }
}
