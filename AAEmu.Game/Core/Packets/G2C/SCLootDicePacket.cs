using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLootDicePacket : GamePacket
{
    private readonly Item _item;

    /// <summary>
    /// Send a roll request for a given loot item
    /// </summary>
    /// <param name="item">Loot item to roll for, can be identified by its fictional itemId</param>
    public SCLootDicePacket(Item item) : base(SCOffsets.SCLootDicePacket, 5)
    {
        _item = item;
    }

    public override PacketStream Write(PacketStream stream)
    {
        return _item.Write(stream);
    }
}
