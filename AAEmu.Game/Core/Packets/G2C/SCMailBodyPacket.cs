using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMailBodyPacket(
    bool isPrepare,
    bool isSent,
    MailBody body,
    bool isOpenDateModified,
    CountUnreadMail count)
    : GamePacket(SCOffsets.SCMailBodyPacket, 5)
{
    //private readonly ulong _mailID;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isPrepare);
        stream.Write(isSent);
        stream.Write(body);
        stream.Write(isOpenDateModified);
        stream.Write(count);
        return stream;
    }
}
