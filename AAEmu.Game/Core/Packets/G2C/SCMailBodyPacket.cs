using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Publishes a mail body together with updated mailbox counters.</summary>
public class SCMailBodyPacket(
    bool isPrepare,
    bool isSent,
    MailBody body,
    bool isOpenDateModified,
    CountUnreadMail count)
    : GamePacket(SCOffsets.SCMailBodyPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isPrepare);
        stream.Write(isSent);
        stream.Write(body);
        stream.Write(0ul); // extra
        stream.Write(isOpenDateModified);
        stream.Write(count);
        return stream;
    }
}
