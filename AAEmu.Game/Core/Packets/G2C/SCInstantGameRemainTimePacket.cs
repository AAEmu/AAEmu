using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInstantGameRemainTimePacket(uint remainTime) : GamePacket(SCOffsets.SCInstantGameUnkPacket, 5)
{
    // TODO: Check offset!!

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(remainTime);
        return stream;
    }
}