using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZUnitPoints (0x020) — World → Zone HP/MP sync.
/// </summary>
public class WZUnitPointsPacket(uint objId, int health, int mana) : ZonePacket(WzOpcodes.UnitPoints)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write((long)health * 100);
        stream.Write((long)mana * 100);
    }
}
