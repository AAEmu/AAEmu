using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// u64(master) at +0x10, masterWorldId s8 at +0x18, bc(slave) at +0x1C.
/// </summary>
public class SCSlaveBoundPacket(uint masterId, sbyte masterWorldId, uint slaveId)
    : GamePacket(SCOffsets.SCSlaveBoundPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)masterId);
        stream.Write(masterWorldId);
        stream.WriteBc(slaveId);
        return stream;
    }
}
