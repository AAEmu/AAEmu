using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAchievementItemSentPacket(uint id, bool byMail) : GamePacket(SCOffsets.SCAchievementItemSentPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);     // type
        stream.Write(byMail); // byMail

        return stream;
    }
}
