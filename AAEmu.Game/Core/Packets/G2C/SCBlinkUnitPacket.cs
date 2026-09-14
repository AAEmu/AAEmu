using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBlinkUnitPacket(uint objId, float distance, float degree, bool move3D, float x, float y, float z)
    : GamePacket(SCOffsets.SCBlinkUnitPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(distance);
        stream.Write(degree);
        // Wire layout: a move3D flag sits between the degree pair and the position. Without it the body
        // is one byte short and the position is parsed shifted, which loses the blink entirely.
        stream.Write(move3D);
        stream.Write(Helpers.ConvertLongX(x));
        stream.Write(Helpers.ConvertLongY(y));
        stream.Write(z);
        return stream;
    }
}
