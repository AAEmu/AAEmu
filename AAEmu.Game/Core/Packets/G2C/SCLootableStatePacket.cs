using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLootableStatePacket : GamePacket
{
    private readonly LootOwnerType _lootOwnerType; // Might be the same as QuestAcceptorType
    private readonly uint _lootOwnerObjId;
    private readonly bool _hasLoot;

    /// <summary>
    /// Sends the loot-able state of an object or unit
    /// </summary>
    /// <param name="lootOwnerType">What type of object has the loot state set</param>
    /// <param name="lootOwnerObjId">ObjectId to set the state for</param>
    /// <param name="hasLoot"></param>
    public SCLootableStatePacket(LootOwnerType lootOwnerType, uint lootOwnerObjId, bool hasLoot) : base(SCOffsets.SCLootableStatePacket, 1)
    {
        _lootOwnerType = lootOwnerType;
        _lootOwnerObjId = lootOwnerObjId;
        _hasLoot = hasLoot;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ushort)0); // unused itemIndex?
        stream.Write((ushort)_lootOwnerType);
        stream.WriteBc(_lootOwnerObjId);
        stream.Write((byte)0);
        stream.Write(_hasLoot);
        return stream;
    }
}
