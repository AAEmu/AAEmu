using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMailReturnedPacket : GamePacket
{
    private readonly long _mailId;
    private readonly MailHeader _mail;
    private readonly CountUnreadMail _count;

    public SCMailReturnedPacket(long mailId, MailHeader mail, CountUnreadMail count) : base(SCOffsets.SCMailReturnedPacket, 5)
    {
        _mailId = mailId;
        _mail = mail;
        _count = count;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_mailId);
        _mail.Write(stream);
        _count.Write(stream);
        return stream;
    }
}
