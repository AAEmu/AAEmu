using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCServerFileTimeSyncPacket(long worldFileTime, int timeZoneBias)
    : GamePacket(SCOffsets.SCServerFileTimeSyncPacket, 1)
{
    // 10.0.2.13 serializer 0x39A95080 reads an i64 time followed by this signed i32;
    // handler 0x393410E0 stores the bias without negating it;
    // XlGetWorldLocalTime 0x33023160 calculates world-local time as UTC - bias.
    // Send Windows-style minutes west of UTC, the inverse of TimeZoneInfo's UTC offset.
    public SCServerFileTimeSyncPacket()
        : this(Helpers.UnixTimeNow(), GetClientTimeZoneBias(TimeZoneInfo.Local, DateTime.UtcNow)) { }

    internal static int GetClientTimeZoneBias(TimeZoneInfo timeZone, DateTime utcNow) =>
        -(int)timeZone.GetUtcOffset(utcNow).TotalMinutes;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldFileTime);
        stream.Write(timeZoneBias);
        return stream;
    }
}
