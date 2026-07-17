using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCGradeEnchantBroadcastPacket(string charName, byte result, Item item, byte type1, byte type2)
    : GamePacket(SCOffsets.SCGradeEnchantBroadcastPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(charName);
        stream.Write(result);
        stream.Write(item);
        stream.Write(type1);
        stream.Write(type2);

        return stream;
    }
}
