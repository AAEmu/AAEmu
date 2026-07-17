using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCEscapeSlavePacket(uint unitId, float x, float y, float z, float rot)
    : GamePacket(SCOffsets.SCEscapeSlavePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(Helpers.ConvertLongX(x));
        stream.Write(Helpers.ConvertLongY(y));
        stream.Write(z);
        stream.Write(rot);
        return stream;
    }
}
