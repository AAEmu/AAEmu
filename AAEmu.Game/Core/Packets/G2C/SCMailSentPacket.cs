using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMailSentPacket(MailHeader mail, (SlotType slotType, byte slot)[] items)
    : GamePacket(SCOffsets.SCMailSentPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(mail);
        foreach (var (slotType, slot) in items) // TODO 10 items
        {
            stream.Write((byte)slotType);
            stream.Write(slot);
        }

        return stream;
    }
}
