using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Notifies the client that a previously disabled item had its enchantment restored.
/// Wire: full item view followed by two trailing bytes (initial grade / current grade),
/// mirroring the other grade-enchant result packets.
/// </summary>
public class SCRestoreDisableEnchantPacket(Item item, byte type1, byte type2)
    : GamePacket(SCOffsets.SCRestoreDisableEnchantPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(item);
        stream.Write(type1);
        stream.Write(type2);

        return stream;
    }
}
