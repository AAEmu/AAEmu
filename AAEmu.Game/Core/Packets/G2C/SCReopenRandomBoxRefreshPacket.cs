using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Acknowledges one reopen-box refresh request.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 serializer, which passes each value's
/// name alongside the value: s16 ErrorMessage only - the same fixed shape as the random shop
/// refresh acknowledgement. Only the success (no-error) acknowledgement is sent; the failure
/// codes for this family are not decoded in the corpus.
/// </remarks>
public class SCReopenRandomBoxRefreshPacket(short errorMessage)
    : GamePacket(SCOffsets.SCReopenRandomBoxRefreshPacket, 1)
{
    public short ErrorMessage { get; } = errorMessage;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ErrorMessage);
        return stream;
    }
}
