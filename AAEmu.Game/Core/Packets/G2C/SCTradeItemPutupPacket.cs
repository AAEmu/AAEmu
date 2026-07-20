using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTradeItemPutupPacket(SlotType slotType, byte slot, int amount)
    : GamePacket(SCOffsets.SCTradeItemPutupPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)slotType); // type
        stream.Write(slot); // index
        stream.Write(amount); // amount
        return stream;
    }

    /*
       this[12] = 0;
       this[14] = 0;
       a2->Reader->ReadByte("type", this + 13, 0);
       a2->Reader->ReadByte("index", v2 + 15, 0);
       return a2->Reader->ReadInt32("amount", v2 + 16, 0);
     */
}
