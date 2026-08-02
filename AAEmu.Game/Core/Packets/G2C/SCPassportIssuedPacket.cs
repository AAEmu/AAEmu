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
public class SCPassportIssuedPacket(sbyte target, long key, long pass, int ip, short port, int family, int @type, int @type2) : GamePacket(SCOffsets.SCPassportIssuedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(target);
        stream.Write(key);
        stream.Write(pass);
        stream.Write(ip);
        stream.Write(port);
        stream.Write(family);
        stream.Write(@type);
        stream.Write(@type2);
        return stream;
    }
}
