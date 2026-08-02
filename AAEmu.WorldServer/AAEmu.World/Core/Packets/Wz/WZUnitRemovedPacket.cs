using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>WZUnitRemoved (0x008) — leave zone / despawn unit.</summary>
public class WZUnitRemovedPacket(uint bcId) : ZonePacket(WzOpcodes.UnitRemoved)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(bcId);
    }
}
