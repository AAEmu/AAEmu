using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// </summary>
/// <remarks>
/// The u64 between the unread count and hasBody is new, and the trailing isCancel the v1.2 shape
/// wrote is gone. Placing a house updates its tax mail, so every placement sent one of these and the
/// client failed it on "extra" — a stream error that costs whatever else shared the batch.
/// </remarks>
public class SCGotMailPacket(MailHeader mail, CountUnreadMail count, MailBody body = null)
    : GamePacket(SCOffsets.SCGotMailPacket, 1)
{
    private readonly bool _hasBody = body != null;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(mail);
        stream.Write(count);
        stream.Write(0ul); // TODO(v10): extra — the binary leaves it unnamed beyond the field name
        stream.Write(_hasBody);
        if (_hasBody)
            stream.Write(body);
        return stream;
    }
}
