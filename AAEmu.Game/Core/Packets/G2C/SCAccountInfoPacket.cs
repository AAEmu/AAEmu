using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAccountInfoPacket(int payMethod, int payLocation, DateTime payStart, DateTime payEnd)
    : GamePacket(SCOffsets.SCAccountInfoPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(payMethod);
        stream.Write(payLocation);
        stream.Write(payStart);
        stream.Write(payEnd);
        stream.Write((long)0); // realPayTime (+152, 8 bytes)
        stream.Write((uint)0); // buyPremiumCount (u32) — added in 10.0.2.13 (client deserializer sub_39A91870)
        return stream;
    }
}
