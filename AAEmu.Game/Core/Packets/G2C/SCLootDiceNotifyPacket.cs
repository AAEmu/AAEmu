using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLootDiceNotifyPacket : GamePacket
{
    private readonly string _charName;
    private readonly sbyte _dice;
    private readonly Item _item;

    public SCLootDiceNotifyPacket(string charName, sbyte dice, Item item = null) : base(SCOffsets.SCLootDiceNotifyPacket, 5)
    {
        _charName = charName;
        _dice = dice;
        _item = item;
    }
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_charName);
        stream.Write(_item);
        stream.Write(_dice);
        return stream;
    }
}
