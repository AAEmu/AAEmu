using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Whether sensitive operations (item destruction, trading, mailing) are currently protected, and
/// for how much longer. Sent in answer to the client's request for that state.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: a single byte flag followed by the remaining time in seconds.
/// </remarks>
public class SCProtectSensitiveOperationResultPacket(byte protectSensitiveOperation, uint remainTime)
    : GamePacket(SCOffsets.SCProtectSensitiveOperationResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(protectSensitiveOperation);
        stream.Write(remainTime);
        return stream;
    }
}
