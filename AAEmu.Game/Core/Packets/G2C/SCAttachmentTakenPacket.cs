using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAttachmentTakenPacket(
    long mailId,
    bool money,
    bool aaPoint,
    bool takeSequentially,
    List<ItemIdAndLocation> itemsList)
    : GamePacket(SCOffsets.SCAttachmentTakenPacket, 5)
{
    // public readonly ulong[] _itemId;
    // public readonly (SlotType slotType, byte slot)[] _itemSlots;

    // public SCAttachmentTakenPacket(long mailId, bool money, bool aaPoint, bool takeSequentially, ulong[] itemId, (SlotType slotType, byte slot)[] itemSlots) : base(SCOffsets.SCAttachmentTakenPacket, 5)
    //_itemId = itemId;
    //_itemSlots = itemSlots;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(mailId);
        stream.Write(money);
        stream.Write(aaPoint);
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
        /*
        stream.Write((byte)_itemId.Length);
        for (int i = 0; i < 10; i++)
        {
            if (_itemId.Length != 0 && i < _itemId.Length)
                stream.Write(_itemId[i]);

            if (i < _itemSlots.Length)
            {
                stream.Write((byte)_itemSlots[i].slotType);
                stream.Write(_itemSlots[i].slot);
            }
            else
            {
                stream.Write((byte)SlotType.None);
                stream.Write(0);
            }
        }
        */

        return stream;
    }
}
