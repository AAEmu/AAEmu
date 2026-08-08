using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Completes a mailbox listing and publishes its counters.</summary>
public class SCMailListEndPacket(byte mailBoxListKind, CountUnreadMail count)
    : GamePacket(SCOffsets.SCMailListEndPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(mailBoxListKind);
        stream.Write(count);
        return stream;
    }
}
