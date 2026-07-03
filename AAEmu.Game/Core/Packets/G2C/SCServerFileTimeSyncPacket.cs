using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCServerFileTimeSyncPacket(long worldFileTime, int timeZoneBias)
    : GamePacket(SCOffsets.SCServerFileTimeSyncPacket, 1)
{
    // Body: "worldFileTime" u64 + "timeZoneBais" i32.
    // worldFileTime = Unix seconds; timeZoneBias = the
    // server's UTC offset in minutes (capture: -480 = UTC-8).
    public SCServerFileTimeSyncPacket()
        : this(Helpers.UnixTimeNow(), (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes) { }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(worldFileTime);
        stream.Write(timeZoneBias);
        return stream;
    }
}
