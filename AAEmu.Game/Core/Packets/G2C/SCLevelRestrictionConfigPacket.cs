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
    : GamePacket(SCOffsets.SCLevelRestrictionConfigPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(searchLevel);
        stream.Write(bidLevel);
        stream.Write(postLevel);
        stream.Write(trade);
        stream.Write(mail);
        for (var i = 0; i < 15; i++)
        {
            stream.Write(limitLevels[i]);
        }
        return stream;
    }
}
