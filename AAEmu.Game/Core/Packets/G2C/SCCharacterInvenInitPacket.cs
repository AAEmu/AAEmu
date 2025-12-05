using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterInvenInitPacket(uint numInvenSlots, uint numBankSlots)
    : GamePacket(SCOffsets.SCCharacterInvenInitPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(numInvenSlots);
        stream.Write(numBankSlots);
        return stream;
    }
}
