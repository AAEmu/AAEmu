using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLevelRestrictionConfigPacket(
    byte searchLevel,
    byte bidLevel,
    byte postLevel,
    byte trade,
    byte mail,
    byte[] limitLevels)
    : GamePacket(SCOffsets.SCLevelRestrictionConfigPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // 10.0.2.13 (client deserializer sub_393A49B0): searchLevel, bidLevel, postLevel, trade, mail, perm,
        // others (7 named u8) followed by a 20-element u8 loop = 27 bytes total.
        stream.Write(searchLevel);
        stream.Write(bidLevel);
        stream.Write(postLevel);
        stream.Write(trade);
        stream.Write(mail);
        stream.Write((byte)0); // perm
        stream.Write((byte)0); // others
        for (var i = 0; i < 20; i++)
            stream.Write(i < limitLevels.Length ? limitLevels[i] : (byte)0);
        return stream;
    }
}
