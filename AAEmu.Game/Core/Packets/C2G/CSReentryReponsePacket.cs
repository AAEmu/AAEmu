using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>Client's reply to the re-entry check: whether it cancels the pending re-entry.</summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSReentryReponsePacket() : GamePacket(CSOffsets.CSReentryReponsePacket, 1)
{
    private const int BodySize = sizeof(byte); // u8 cancel (catalog_CS.md 0x12D)

    public bool Cancel { get; private set; }

    public override void Read(PacketStream stream)
    {
        // The pinned shape is exactly one u8: a truncated frame would otherwise read as
        // "cancel = false" from the overrun default, i.e. a silent wrong answer.
        if (stream.LeftBytes != BodySize)
            throw new InvalidDataException($"Expected a {BodySize}-byte re-entry response body (u8 cancel), got {stream.LeftBytes} bytes.");

        Cancel = stream.ReadBoolean();
    }
}
