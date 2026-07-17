using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCNationalMonumentChangedPacket(ushort id, long type, float x, float y, float z)
    : GamePacket(SCOffsets.SCNationalMonumentChangedPacket, 5)
{
    // TODO id?

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(type);
        stream.Write(Helpers.ConvertLongX(x));
        stream.Write(Helpers.ConvertLongY(y));
        stream.Write(z);
        return stream;
    }
}
