using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAttachmentTakenPacket : GamePacket
{
    private readonly long _mailId;
    private readonly bool _money;
    private readonly bool _aaPoint;
    private readonly bool _takeSequentially;
    // public readonly ulong[] _itemId;
    // public readonly (SlotType slotType, byte slot)[] _itemSlots;
    private readonly List<ItemIdAndLocation> _itemsList;

    // public SCAttachmentTakenPacket(long mailId, bool money, bool aaPoint, bool takeSequentially, ulong[] itemId, (SlotType slotType, byte slot)[] itemSlots) : base(SCOffsets.SCAttachmentTakenPacket, 5)
    public SCAttachmentTakenPacket(long mailId, bool money, bool aaPoint, bool takeSequentially, List<ItemIdAndLocation> itemsList)
        : base(SCOffsets.SCMailAttachmentTakenPacket, 5)
    {
        _mailId = mailId;
        _money = money;
        _aaPoint = aaPoint;
        _takeSequentially = takeSequentially;
        //_itemId = itemId;
        //_itemSlots = itemSlots;
        _itemsList = itemsList;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_mailId);
        stream.Write(_money);
        stream.Write(_aaPoint);
        stream.Write(_takeSequentially);
        stream.Write((byte)_itemsList.Count);
        foreach (var item in _itemsList)
        {
            stream.Write(item.Id);
        }
        for (var i = 0; i < 10; i++)
        {
            if (i < _itemsList.Count)
            {
                var item = _itemsList[i];
                stream.Write((byte)item.SlotType);
                stream.Write(item.Slot);
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
