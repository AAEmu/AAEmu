using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Indun;

/// <summary>
/// The two live readings an <c>indun_event_npc_info_broadcastings</c> row can ask for, and how each becomes
/// an <c>npcInfo</c> row of SCIndunPlayingInfoBroadcastingPacket.
/// </summary>
/// <remarks>
/// <c>enum_indun_npc_info_broadcasting_types</c>: 1 a buff stack count, 2 a buff's remaining time. The
/// stack form reports the count and no bound (the buff's own maximum is the client's business); the time
/// form reports milliseconds left and the buff's whole duration in milliseconds. An unknown type reports
/// nothing, so a new kind of readout is skipped rather than sent as a wrong number.
/// </remarks>
/// <remarks>
/// The time form is milliseconds because the client's own readout divides by 1000 before it displays
/// (<c>indun_playing_info</c> does <c>data.time = v2.leftTime / 1000</c>). Whole seconds therefore arrive
/// as a ~0 reading and the countdown never moves.
/// </remarks>
public static class IndunPlayingInfoRules
{
    public const byte BroadcastingStackCount = 1;
    public const byte BroadcastingRemainingTime = 2;

    public static bool TryReadValues(
        byte broadcastingId,
        int stackCount,
        int remainingMs,
        int durationMs,
        out uint value,
        out uint limit)
    {
        switch (broadcastingId)
        {
            case BroadcastingStackCount:
                value = (uint)Math.Max(0, stackCount);
                limit = 0u;
                return true;
            case BroadcastingRemainingTime:
                value = (uint)Math.Max(0, remainingMs);
                limit = (uint)Math.Max(0, durationMs);
                return true;
            default:
                value = 0u;
                limit = 0u;
                return false;
        }
    }

    /// <summary>The reading of <paramref name="buff"/> for a row of the given broadcasting type.</summary>
    public static bool TryReadBuff(byte broadcastingId, Buff buff, out uint value, out uint limit)
    {
        if (buff == null)
            return TryReadAbsentBuff(broadcastingId, out value, out limit);

        return TryReadValues(broadcastingId, buff.Stack, (int)buff.GetTimeLeft(), buff.Duration, out value, out limit);
    }

    /// <summary>
    /// What a set of readout rows looks like as the client displays it: each row's buff, its count, and a
    /// running timer's duration and whole seconds left. The client shows the last reading as-is and does
    /// not tick a timer down between packets, so every displayed second is a change; readings within the
    /// same second are not.
    /// </summary>
    public static string Shape(IEnumerable<IndunPlayingInfoNpc> rows) =>
        string.Join(";", rows.Select(r => r.Limit != 0
            ? $"{r.NpcId}/{r.BuffId}:t{r.Limit}:{r.Value / 1000}"
            : $"{r.NpcId}/{r.BuffId}:{r.Value}"));

    /// <summary>
    /// The reading of a row whose buff is not up yet — the unit does not carry it, or is not in the copy at
    /// all. Every known type reads zero: the instance HUD shows all zeros until the copy's script applies
    /// the buffs, and a time readout only counts once its buff is running.
    /// </summary>
    public static bool TryReadAbsentBuff(byte broadcastingId, out uint value, out uint limit) =>
        TryReadValues(broadcastingId, 0, 0, 0, out value, out limit);
}
