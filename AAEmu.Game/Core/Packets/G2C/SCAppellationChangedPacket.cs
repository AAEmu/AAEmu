using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAppellationChangedPacket(uint objId, uint appellationId, uint appellationStampId)
    : GamePacket(SCOffsets.SCAppellationChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(appellationId);
        stream.Write(appellationStampId);
        return stream;
    }
}
