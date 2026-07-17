using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMailReturnedPacket(long mailId, MailHeader mail) : GamePacket(SCOffsets.SCMailReturnedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(mailId);
        stream.Write(mail);
        return stream;
    }
}
