using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZUnitDeath (0x021) — World → Zone: mark unit dead.
/// Required for zone-mirror kills so Zone liveCount/corpse/respawn path runs.
/// </summary>
public class WZUnitDeathPacket(uint bcId) : ZonePacket(WzOpcodes.UnitDeath)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(bcId);
    }
}
