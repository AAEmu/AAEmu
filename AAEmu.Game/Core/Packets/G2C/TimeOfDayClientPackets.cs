using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

using NLog;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Which time-of-day packet the client may see.
/// </summary>
/// <remarks>
/// The hour-only packet is the clock. The first one force-applies lighting and
/// water, so it must land before the world load — not at spawn. Later hour
/// packets ease. The four-field packet reapplies environment and does not bind
/// the hour — do not send it on enter or on the periodic tick. Open-world
/// speed/start/end are the client defaults.
/// </remarks>
public static class TimeOfDayClientPackets
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    // Which hour the client is handed, and from which path, is logged: the first hour packet force-applies
    // the lighting, so this is what decides whether a map is lit the way its level authors it.
    public static SCTimeOfDayPacket Hour(float hour)
    {
        Logger.Debug("SCTimeOfDay → client hour={0:F3} (hour)", hour);
        return new SCTimeOfDayPacket(hour);
    }

    public static SCTimeOfDayPacket Periodic(float hour)
    {
        Logger.Debug("SCTimeOfDay → client hour={0:F3} (shared-day tick)", hour);
        return new SCTimeOfDayPacket(hour);
    }

    public static SCTimeOfDayPacket FromZoneReport(float hour)
    {
        Logger.Debug("SCTimeOfDay → client hour={0:F3} (zone report)", hour);
        return new SCTimeOfDayPacket(hour);
    }

    public static SCDetailedTimeOfDayPacket EnvironmentSeed(float hour) =>
        new(hour, TimeManager.DefaultGameHourSpeed, 0f, 24f);

    /// <summary>
    /// First hour bind force-applies lighting and water. Send it before the
    /// client opens the world load — not after the ocean already exists.
    /// </summary>
    public static void BindBeforeWorldLoad(Action<GamePacket> send, float hour)
    {
        ArgumentNullException.ThrowIfNull(send);
        Logger.Debug("SCTimeOfDay → client hour={0:F3} (before world load)", hour);
        send(new SCTimeOfDayPacket(hour));
    }

    /// <summary>
    /// Catch-up after the hour is already bound. Same opcode; later packets
    /// ease toward the server hour instead of force-applying.
    /// </summary>
    public static void SendEnterWorld(Action<GamePacket> send, float hour)
    {
        ArgumentNullException.ThrowIfNull(send);
        Logger.Debug("SCTimeOfDay → client hour={0:F3} (enter world)", hour);
        send(new SCTimeOfDayPacket(hour));
    }
}
