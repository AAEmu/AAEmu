using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMailListPacket(bool isSent, MailHeader[] mails) : GamePacket(SCOffsets.SCMailListPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isSent);
        stream.Write(mails.Length);
        foreach (var mail in mails)
            stream.Write(mail);
        return stream;
    }
}
