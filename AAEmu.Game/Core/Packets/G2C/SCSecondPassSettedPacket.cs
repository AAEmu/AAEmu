using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCSecondPassSettedPacket(bool passed, long accountUnlockTime, long clearReserveTime, sbyte failedCount) : GamePacket(SCOffsets.SCSecondPassSettedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(passed);
        stream.Write(accountUnlockTime);
        stream.Write(clearReserveTime);
        stream.Write(failedCount);
        return stream;
    }
}
