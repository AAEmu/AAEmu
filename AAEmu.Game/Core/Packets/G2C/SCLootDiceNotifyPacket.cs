using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLootDiceNotifyPacket : GamePacket
{
    private readonly string _charName;
    private readonly Item _item;
    private readonly sbyte _dice;

    /// <summary>
    /// Notify player of a roll result of any eligible players for a given item
    /// </summary>
    /// <param name="charName"></param>
    /// <param name="item"></param>
    /// <param name="dice"></param>
    public SCLootDiceNotifyPacket(string charName, Item item, sbyte dice) : base(SCOffsets.SCLootDiceNotifyPacket, 5)
    {
        _charName = charName;
        _item = item;
        _dice = dice;
    }
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_charName);
        stream.Write(_item);
        stream.Write(_dice);
        return stream;
    }
}
