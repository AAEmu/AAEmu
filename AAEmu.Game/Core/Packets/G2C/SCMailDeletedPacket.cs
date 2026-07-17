using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMailDeletedPacket(bool isSent, long mailId, bool isUnreadMailCountModified, CountUnreadMail count)
    : GamePacket(SCOffsets.SCMailDeletedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isSent);
        stream.Write(mailId);
        stream.Write(isUnreadMailCountModified);
        stream.Write(count);
        return stream;
    }
}
