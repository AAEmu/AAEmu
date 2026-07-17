using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInvenExpandedPacket(SlotType slotType, byte numSlots) : GamePacket(SCOffsets.SCInvenExpandedPacket, 5)
{
    private readonly byte _slotType = (byte)slotType;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_slotType);
        stream.Write(numSlots);
        return stream;
    }
}
