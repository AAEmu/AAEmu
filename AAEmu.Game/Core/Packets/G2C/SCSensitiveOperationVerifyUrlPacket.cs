using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The URL the client's sensitive-operation window has to open so the player can verify the
/// operation in a browser.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: a sequence number followed by the URL.
/// </remarks>
public class SCSensitiveOperationVerifyUrlPacket(uint seqNum, string url)
    : GamePacket(SCOffsets.SCSensitiveOperationVerifyUrlPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(seqNum);
        stream.Write(url ?? string.Empty);
        return stream;
    }
}
