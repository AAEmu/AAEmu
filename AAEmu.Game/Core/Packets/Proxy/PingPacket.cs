using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class PingPacket() : GamePacket(PPOffsets.PingPacket, 2)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off; // keepalive — keep out of logs

    // Server send timestamp (ms). Captured at construction so each ping carries a monotonic value.
    private readonly long _tm = Environment.TickCount64;

    public override void Read(PacketStream stream)
    {
        var tm = stream.ReadInt64(); // tPhy
        var when = stream.ReadInt64(); // ping
        var local = stream.ReadUInt32();

        Connection.LastPing = DateTime.UtcNow;
        if (Connection.ActiveChar != null)
            Connection.ActiveChar.LastPacketActivityTime = DateTime.UtcNow;

        Connection.SendPacket(new PongPacket(tm, when, local));
    }

    /// <summary>
    /// Server-initiated keepalive ping. The real server sends X2::PingPacket (type 0x12, level 2)
    /// on a periodic timer — crynetwork_dedicate.dll scheduler sub_3957FCC0 → construct+send
    /// sub_39579F10 (writes type=0x12, sends at level 2 via sub_39579CE0). The 10.0.2.13 client's
    /// game-channel recv watchdog (crynetwork.dll sub_3956A200 → "recv exception: internal 4 wsa 258"
    /// = WAIT_TIMEOUT) drops conn 1239 ~2s after the char-list unless it keeps receiving server data.
    /// Level 2 bypasses the encryption gate (client dispatcher sub_39574230 case 2 has no gate, unlike
    /// case 1 / level 1 which rejects plain non-{153,154,387} types as "encryption-mv"), so a plain
    /// ping is always accepted and resets the watchdog. Body matches the client Ping deserializer
    /// sub_39576750: tm(i64) + when(i64) + local(u32) = 20 bytes.
    /// </summary>
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_tm); // tPhy
        stream.Write(_tm); // ping (when)
        stream.Write((uint)Environment.TickCount); // local

        return stream;
    }
}
