using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSlaveBoundPacket(uint masterId, uint slaveId) : GamePacket(SCOffsets.SCSlaveBoundPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(masterId);
        stream.WriteBc(slaveId);
        return stream;
    }
}
