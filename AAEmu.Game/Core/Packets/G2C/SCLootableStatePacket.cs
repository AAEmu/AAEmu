using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLootableStatePacket : GamePacket
{
    private readonly LootOwnerType _lootOwnerType;
    private readonly uint _lootOwnerObjId;
    private readonly bool _hasLoot;

    public SCLootableStatePacket(LootOwnerType lootOwnerType, uint lootOwnerObjId, bool hasLoot) : base(SCOffsets.SCLootableStatePacket, 1)
    {
        _lootOwnerType = lootOwnerType;
        _lootOwnerObjId = lootOwnerObjId;
        _hasLoot = hasLoot;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // iid packing matches LootingContainer item ids: (objId<<32)|(type<<16)|index
        var iid = ((ulong)_lootOwnerObjId << 32) | ((ulong)(ushort)_lootOwnerType << 16);
        stream.Write(iid);
        stream.Write(_hasLoot);
        return stream;
    }
}
