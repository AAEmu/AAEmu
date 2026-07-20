using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCountUnreadMailPacket(CountUnreadMail count) : GamePacket(SCOffsets.SCCountUnreadMailPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(count);
        return stream;
    }
}
