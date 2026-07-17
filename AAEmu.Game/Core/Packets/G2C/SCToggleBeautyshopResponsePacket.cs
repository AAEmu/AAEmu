using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCToggleBeautyshopResponsePacket(byte state) : GamePacket(SCOffsets.SCToggleBeautyshopResponsePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(state);
        return stream;
    }
}
