using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCWorldAABBPacket(ulong x, ulong y, float z, float vX, float vY, float vZ, float w, float scale, float aabbminX, float aabbminY, float aabbminZ, float aabbmaxX, float aabbmaxY, float aabbmaxZ) : GamePacket(SCOffsets.SCWorldAABBPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(x);
        stream.Write(y);
        stream.Write(z);
        stream.Write(vX);
        stream.Write(vY);
        stream.Write(vZ);
        stream.Write(w);
        stream.Write(scale);
        stream.Write(aabbminX);
        stream.Write(aabbminY);
        stream.Write(aabbminZ);
        stream.Write(aabbmaxX);
        stream.Write(aabbmaxY);
        stream.Write(aabbmaxZ);
        return stream;
    }
}
