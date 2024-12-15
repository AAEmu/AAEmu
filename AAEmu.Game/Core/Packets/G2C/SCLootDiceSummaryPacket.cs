using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLootDiceSummaryPacket : GamePacket
{
    private readonly ushort _itemIndex;
    private readonly LootOwnerType _lootOwnerType;
    private readonly uint _lootOwnerObjId;
    //private readonly ulong _iId;
    private readonly Dictionary<uint, sbyte> _diceList;

    public SCLootDiceSummaryPacket(LootOwnerType lootOwnerType, uint lootOwner, ushort itemIndex, Dictionary<uint,sbyte> diceList) : base(SCOffsets.SCLootDiceSummaryPacket, 1)
    {
        _itemIndex = itemIndex;
        _lootOwnerType = lootOwnerType;
        _lootOwnerObjId = lootOwner;
        _diceList = diceList;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_itemIndex);
        stream.Write((ushort)_lootOwnerType);
        stream.WriteBc(_lootOwnerObjId);
        stream.Write((byte)0);
        stream.Write(_diceList.Count);
        foreach (var (player, dice) in _diceList)
        {
            stream.Write(player);
            stream.Write(dice);
        }
        return stream;
    }
}
