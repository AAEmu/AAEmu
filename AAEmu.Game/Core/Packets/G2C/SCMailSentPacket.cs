using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMailSentPacket(bool groupSending, MailHeader mail, CountUnreadMail count, (SlotType slotType, byte slot)[] items)
    : GamePacket(SCOffsets.SCMailSentPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Client reader FUN_39a9ecf0: bool groupSending, MailHeader, CountUnreadMail,
        // then always 10 x (u8 slotType, u8 slot). The header alone desyncs the client,
        // so the leading flag and the counters are load-bearing, not decorative.
        stream.Write(groupSending);
        stream.Write(mail);
        stream.Write(count);
        for (var i = 0; i < MailBody.MaxMailAttachments; i++)
        {
            if (i < items.Length)
            {
                stream.Write((byte)items[i].slotType);
                stream.Write(items[i].slot);
            }
            else
            {
                stream.Write((byte)0);
                stream.Write((byte)0);
            }
        }

        return stream;
    }
}
