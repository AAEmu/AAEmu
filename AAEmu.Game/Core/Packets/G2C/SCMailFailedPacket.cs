using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMailFailedPacket(MailResult err, (SlotType slotType, byte slot)[] items, bool money)
    : GamePacket(SCOffsets.SCMailFailedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)err);
        foreach (var (slotType, slot) in items) // TODO 10 items
        {
            stream.Write((byte)slotType);
            stream.Write(slot);
        }

        stream.Write(money);
        return stream;
    }
}
