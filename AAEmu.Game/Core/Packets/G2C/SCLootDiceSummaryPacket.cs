using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Loots;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLootDiceSummaryPacket(LootOwnerType lootOwnerType, uint lootOwner, ushort itemIndex, Dictionary<Character, sbyte> diceList) : GamePacket(SCOffsets.SCLootDiceSummaryPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(itemIndex);
        stream.Write((ushort)lootOwnerType);
        stream.WriteBc(lootOwner);
        stream.Write((byte)0);
        stream.Write(diceList.Count);
        foreach (var (player, dice) in diceList)
        {
            stream.Write(player.Id);
            stream.Write(dice);
        }
        return stream;
    }
}
