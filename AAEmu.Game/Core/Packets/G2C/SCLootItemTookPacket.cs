using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLootItemTookPacket : GamePacket
{
    private readonly uint _itemTemplateId;
    private readonly ushort _itemIndex;
    private readonly LootOwnerType _lootOwnerType;
    private readonly uint _lootOwnerId;
    private readonly int _count;

    public SCLootItemTookPacket(uint itemTemplateId, ushort itemIndex, LootOwnerType lootOwnerType, uint lootOwnerId, ulong iId, int count) : base(SCOffsets.SCLootItemTookPacket, 1)
    {
        _itemTemplateId = itemTemplateId;
        _itemIndex = itemIndex;
        _lootOwnerType = lootOwnerType;
        _lootOwnerId = lootOwnerId;
        _count = count;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_itemTemplateId);
        stream.Write(_itemIndex);
        stream.Write((ushort)_lootOwnerType);
        stream.WriteBc(_lootOwnerId);
        stream.Write((byte)0);
        stream.Write(_count);
        return stream;
    }
}
