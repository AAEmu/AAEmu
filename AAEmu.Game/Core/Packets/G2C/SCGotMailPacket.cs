using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCGotMailPacket(MailHeader mail, CountUnreadMail count, MailBody body = null)
    : GamePacket(SCOffsets.SCGotMailPacket, 1)
{
    private readonly bool _hasBody = body != null;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(mail);
        stream.Write(count);
        stream.Write(0ul);
        stream.Write(_hasBody);
        if (_hasBody)
            stream.Write(body);
        return stream;
    }
}
