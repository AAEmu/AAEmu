using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Tasks.Zones;

using NLog;

namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// Конфликтная зона из таблицы <c>conflict_zones</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>conflict_zones</c>:
/// <list type="bullet">
///   <item><description><c>zone_group_id</c> int → <see cref="ZoneGroupId"/></description></item>
///   <item><description><c>num_kills_0..4</c> int → <see cref="NumKills"/></description></item>
///   <item><description><c>no_kill_min_0..4</c> int → <see cref="NoKillMin"/></description></item>
///   <item><description><c>conflict_min</c> int → <see cref="ConflictMin"/></description></item>
///   <item><description><c>war_min</c> int → <see cref="WarMin"/></description></item>
///   <item><description><c>peace_min</c> int → <see cref="PeaceMin"/></description></item>
///   <item><description><c>peace_protected_faction_id</c> int → <see cref="PeaceProtectedFactionId"/></description></item>
///   <item><description><c>nuia_return_point_id</c> int → <see cref="NuiaReturnPointId"/></description></item>
///   <item><description><c>harihara_return_point_id</c> int → <see cref="HariharaReturnPointId"/></description></item>
///   <item><description><c>war_tower_def_id</c> int → <see cref="WarTowerDefId"/></description></item>
///   <item><description><c>closed</c> bool → <see cref="Closed"/></description></item>
///   <item><description><c>auto_team</c> bool → <see cref="AutoTeam"/></description></item>
///   <item><description><c>auto_team_dismiss</c> bool → <see cref="AutoTeamDismiss"/></description></item>
///   <item><description><c>nation_return_point_id</c> int → <see cref="NationReturnPointId"/></description></item>
///   <item><description><c>pirate_return_point_id</c> int → <see cref="PirateReturnPointId"/></description></item>
///   <item><description><c>war_chaos</c> bool → <see cref="WarChaos"/></description></item>
///   <item><description><c>war_st_hour_0..4</c> int → <see cref="WarStHour"/></description></item>
///   <item><description><c>war_st_min_0..4</c> int → <see cref="WarStMin"/></description></item>
///   <item><description><c>peace_tower_def_id</c> int → <see cref="PeaceTowerDefId"/></description></item>
///   <item><description><c>war_drop_mul</c> real → <see cref="WarDropMul"/></description></item>
///   <item><description><c>war_gold_mul</c> real → <see cref="WarGoldMul"/></description></item>
///   <item><description><c>peace_drop_mul</c> real → <see cref="PeaceDropMul"/></description></item>
///   <item><description><c>peace_gold_mul</c> real → <see cref="PeaceGoldMul"/></description></item>
///   <item><description><c>num_npc_kills_0..4</c> int → <see cref="NumNpcKills"/></description></item>
///   <item><description><c>num_quest_completions_0..4</c> int → <see cref="NumQuestCompletions"/></description></item>
///   <item><description><c>zone_damage_multiplier_kind_id</c> int → <see cref="ZoneDamageMultiplierKindId"/></description></item>
/// </list>
/// </remarks>
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
    public uint PeaceTowerDefId { get; set; }
    public bool Closed { get; set; } = false;
    public bool AutoTeam { get; set; }
    public bool AutoTeamDismiss { get; set; }
    public uint NationReturnPointId { get; set; }
    public uint PirateReturnPointId { get; set; }
    public bool WarChaos { get; set; }
    public int[] WarStHour { get; } = new int[5];
    public int[] WarStMin { get; } = new int[5];
    public float WarDropMul { get; set; }
    public float WarGoldMul { get; set; }
    public float PeaceDropMul { get; set; }
    public float PeaceGoldMul { get; set; }
    public int[] NumNpcKills { get; } = new int[5];
    public int[] NumQuestCompletions { get; } = new int[5];
    public uint ZoneDamageMultiplierKindId { get; set; }

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
