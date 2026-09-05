using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMailReturnedPacket(long mailId, MailHeader mail, CountUnreadMail count) : GamePacket(SCOffsets.SCMailReturnedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Client reader FUN_39a9f110: u64 mailId, MailHeader, CountUnreadMail.
        // Omitting the counters leaves the client reading past the end of the packet.
        stream.Write(mailId);
        stream.Write(mail);
        stream.Write(count);
        return stream;
    }
}
