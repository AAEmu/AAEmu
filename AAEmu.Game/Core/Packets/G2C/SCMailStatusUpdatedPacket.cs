using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMailStatusUpdatedPacket(bool isSent, long mailId, MailStatus status)
    : GamePacket(SCOffsets.SCMailStatusUpdatedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isSent);
        stream.Write(mailId);
        stream.Write((byte)status);
        return stream;
    }
}
