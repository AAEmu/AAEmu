using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLootItemFailedPacket : GamePacket
{
    private readonly int _errorMessage;
    private readonly ushort _itemIndex;
    private readonly LootOwnerType _lootOwnerType;
    private readonly uint _lootOwnerObjId;
    private readonly uint _itemTemplateId;

    public SCLootItemFailedPacket(ErrorMessageType errorMessage, LootOwnerType lootOwnerType, uint lootOwnerObjId, ushort itemIndex, uint itemTemplateId) : base(SCOffsets.SCLootItemFailedPacket, 1)
    {
        _errorMessage = (int)errorMessage;
        _lootOwnerType = lootOwnerType;
        _lootOwnerObjId = lootOwnerObjId;
        _itemIndex = itemIndex;
        _itemTemplateId = itemTemplateId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_errorMessage);
        stream.Write(_itemIndex);
        stream.Write((ushort)_lootOwnerType);
        stream.Write(_lootOwnerObjId);
        stream.Write(_itemTemplateId);
        return stream;
    }
}
