using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnknownPacket(SlotType slotType, byte slot, SlotType slotType2, byte slot2, short errorMessage)
    : GamePacket(SCOffsets.SCUnknownPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)slotType);
        stream.Write(slot);
        stream.Write((byte)slotType2);
        stream.Write(slot2);
        stream.Write(errorMessage);
        return stream;
    }
}
