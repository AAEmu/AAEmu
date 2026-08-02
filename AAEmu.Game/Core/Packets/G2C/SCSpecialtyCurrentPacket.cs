using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Current ratios for one source/destination route. The 128-entry limit and field widths come from
/// </summary>
public class SCSpecialtyCurrentPacket(ushort fromZoneGroup, ushort toZoneGroup, IReadOnlyList<(uint, uint)> results)
    : GamePacket(SCOffsets.SCSpecialtyCurrentPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        if (results.Count > 128)
            throw new ArgumentOutOfRangeException(nameof(results), "A specialty current-ratio response can contain at most 128 entries.");

        stream.Write(results.Count);
        stream.Write(fromZoneGroup);
        stream.Write(toZoneGroup);
        foreach (var (itemId, rate) in results)
        {
            stream.Write(itemId);
            stream.Write(rate);
        }
        return stream;
    }
}
