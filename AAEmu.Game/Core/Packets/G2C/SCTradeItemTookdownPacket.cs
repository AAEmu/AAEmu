using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTradeItemTookdownPacket(SlotType slotType, byte slot)
    : GamePacket(SCOffsets.SCTradeItemTookdownPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)slotType);
        stream.Write(slot);
        return stream;
    }
}
