using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// </summary>
/// <remarks>
/// The packet carries one header, not a counted array — the u32 after isSent is the mailbox total
/// rather than a record count. The trailing mailBoxListKind byte is new in this version, and
/// omitting it left every row a byte short, so the client dropped the list it was building.
/// </remarks>
public class SCMailListPacket(bool isSent, uint total, MailHeader mail, byte mailBoxListKind = 0)
    : GamePacket(SCOffsets.SCMailListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isSent);
        stream.Write(total);
        stream.Write(mail);
        stream.Write(mailBoxListKind); // TODO(v10): kind enum is unnamed in the binary
        return stream;
    }
}
