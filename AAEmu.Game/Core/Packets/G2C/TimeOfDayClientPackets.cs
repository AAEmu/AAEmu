using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

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
    public static SCTimeOfDayPacket Hour(float hour) => new(hour);

    public static SCTimeOfDayPacket Periodic(float hour) => Hour(hour);

    public static SCTimeOfDayPacket FromZoneReport(float hour) => Hour(hour);

    public static SCDetailedTimeOfDayPacket EnvironmentSeed(float hour) =>
        new(hour, TimeManager.DefaultGameHourSpeed, 0f, 24f);

    /// <summary>
    /// First hour bind force-applies lighting and water. Send it before the
    /// client opens the world load — not after the ocean already exists.
    /// </summary>
    public static void BindBeforeWorldLoad(Action<GamePacket> send, float hour)
    {
        ArgumentNullException.ThrowIfNull(send);
        send(Hour(hour));
    }

    /// <summary>
    /// Catch-up after the hour is already bound. Same opcode; later packets
    /// ease toward the server hour instead of force-applying.
    /// </summary>
    public static void SendEnterWorld(Action<GamePacket> send, float hour)
    {
        ArgumentNullException.ThrowIfNull(send);
        send(Hour(hour));
    }
}
