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
public class SCFactionImmigrateLimitDataPacket(uint weekNum, int @type, int @type2, ushort maxCount, ushort curCount) : GamePacket(SCOffsets.SCFactionImmigrateLimitDataPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(weekNum);
        stream.Write(@type);
        stream.Write(@type2);
        stream.Write(maxCount);
        stream.Write(curCount);
        return stream;
    }
}
