using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class PingPacket() : GamePacket(PPOffsets.PingPacket, 2)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Info;

    // Server send timestamp (ms). Captured at construction so each ping carries a monotonic value.
    private readonly long _tm = Environment.TickCount64;

    public override void Read(PacketStream stream)
    {
        var tm = stream.ReadInt64(); // tPhy
        var when = stream.ReadInt64(); // ping
        var local = stream.ReadUInt32();

        Connection.LastPing = DateTime.UtcNow;
        if (Connection.ActiveChar != null)
        {
            Connection.ActiveChar.LastPacketActivityTime = DateTime.UtcNow;
            // DO NOT anchor the physics clock from the ping's `tm`. Empirically (PHYSDIAG 2026-07-19) the
            // client ping carries a CONSTANT tm (~86,392,797) that never advances — it is NOT the client's
            // physics clock. Only CSMoveUnit.Time is the real, advancing client clock. Anchoring from this
            // constant pinned every self/NPC stand's tPhy ~86,000,000 ms away from the client's true clock
            // (~131,000), breaking client-driven movement binding and dropping the connection on spawn.
        }

        Connection.SendPacket(new PongPacket(tm, when, local));
    }

    /// <summary>
    /// Server-initiated keepalive ping. The real server sends X2::PingPacket (type 0x12, level 2)
    /// = WAIT_TIMEOUT) drops conn 1239 ~2s after the char-list unless it keeps receiving server data.
    /// case 1 / level 1 which rejects plain non-{153,154,387} types as "encryption-mv"), so a plain
    /// ping is always accepted and resets the watchdog. Body matches the client Ping deserializer
    /// </summary>
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_tm); // tPhy
        stream.Write(_tm); // ping (when)
        stream.Write((uint)Environment.TickCount); // local

        return stream;
    }
}
