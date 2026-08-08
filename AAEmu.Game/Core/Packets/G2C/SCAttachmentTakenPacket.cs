using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Confirms which coin / points / attachments were pulled from a mail.
/// </summary>
/// <remarks>
/// 10.0.2.13 wire (client reader): mailId (i64), money (bool), aaPoint (bool), honorPointTaken (bool),
/// takeSequentially (bool), count (u8), then itemId[count] (i64 each, ONLY count entries) followed by a
/// fixed slot[10] array of { slotType (u8), slot (u8) }. The v1.2 layout omitted honorPointTaken and
/// interleaved a fixed 10x { itemId, slotType, slot } instead of the separate variable-length id list +
/// fixed slot list. That one missing byte made the client read the item-count out of the item-id bytes
/// (a large value), overrun the packet and drop it, so its "processing the attached item" flag never
/// cleared and no further attachments could be taken.
/// </remarks>
public class SCAttachmentTakenPacket(
    long mailId,
    bool money,
    bool aaPoint,
    bool takeSequentially,
    List<ItemIdAndLocation> itemsList,
    bool honorPointTaken = false)
    : GamePacket(SCOffsets.SCAttachmentTakenPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(mailId);                // i64
        stream.Write(money);                 // bool
        stream.Write(aaPoint);               // bool
        stream.Write(honorPointTaken);       // bool (10.0.2.13: new, before takeSequentially)
        stream.Write(takeSequentially);      // bool
        stream.Write((byte)itemsList.Count); // u8 count

        // Variable-length item-id list: exactly `count` entries.
        foreach (var item in itemsList)
            stream.Write(item.Id);

        // Fixed 10-entry slot list: { slotType (u8), slot (u8) }.
        for (var i = 0; i < 10; i++)
        {
            if (i < itemsList.Count)
            {
                stream.Write((byte)itemsList[i].SlotType);
                stream.Write(itemsList[i].Slot);
            }
            else
            {
                stream.Write((byte)0);
                stream.Write((byte)0);
            }
        }

        return stream;
    }
}
