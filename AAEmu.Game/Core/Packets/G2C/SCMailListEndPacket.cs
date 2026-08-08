using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Signals the end of a mailbox list page.
/// </summary>
/// <remarks>
/// 10.0.2.13 wire is [u8 mailBoxListKind][CountUnreadMail (8x i32)] — NOT the v1.2 (totalHeaders,
/// totalBodies) pair. The client's handler (Mail_ResetStoreOnListEnd) copies the 8-int count block into
/// its HUD and then clears the per-store "list-load-in-flight" flag for the given kind; with the wrong
/// payload the deserialize overran, the handler never ran, and the list stayed stuck on "loading".
/// The kind must match the one the client requested in CSListMail so the correct store is finalized.
/// </remarks>
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
