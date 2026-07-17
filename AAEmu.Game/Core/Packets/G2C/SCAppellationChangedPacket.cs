using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAppellationChangedPacket(uint objId, uint appellationId)
    : GamePacket(SCOffsets.SCAppellationChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(appellationId);
        return stream;
    }
}
