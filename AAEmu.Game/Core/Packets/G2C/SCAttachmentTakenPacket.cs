using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Reports which money and item attachments were taken from a mail.</summary>
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
        stream.Write(mailId);
        stream.Write(money);
        stream.Write(aaPoint);
        stream.Write(honorPointTaken);
        stream.Write(takeSequentially);
        stream.Write((byte)itemsList.Count);
        for (var i = 0; i < 10; i++)
        {
            if (i < itemsList.Count)
            {
                var item = itemsList[i];
                stream.Write(item.Id);
                stream.Write((byte)item.SlotType);
                stream.Write(item.Slot);
            }
            else
            {
                stream.Write((ulong)0);
                stream.Write((byte)0);
                stream.Write((byte)0);
            }
        }

        return stream;
    }
}
