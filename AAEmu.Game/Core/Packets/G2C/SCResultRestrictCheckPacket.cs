using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResultRestrictCheckPacket(long restrictType, byte code, byte result)
    : GamePacket(SCOffsets.SCResultRestrictCheckPacket, 1)
{
    // 10.0.2.13 (binary SCResultRestrictCheck 0x39c28480): restrictType i64 ("type") + code u8 + result u8.
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(restrictType);
        stream.Write(code);
        stream.Write(result);
        return stream;
    }
}
