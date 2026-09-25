using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>One entry in the zone-score list: a kind id and its signed score.</summary>
public readonly record struct ZoneScoreListEntry(uint Type, int Score);

/// <summary>
/// Wire contract: a signed entry count followed by <c>{ uint kind, signed int score }</c> entries.
/// </summary>
/// <remarks>
/// This slice only defines the packet shape; it does not read or mutate score state. A future
/// sender must establish any client-side list bound from live evidence before adding one here.
/// </remarks>
public class SCZoneScoreListPacket(IReadOnlyList<ZoneScoreListEntry> entries)
    : GamePacket(SCOffsets.SCZoneScoreListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var source = entries ?? [];
        stream.Write(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            stream.Write(source[i].Type);
            stream.Write(source[i].Score);
        }
        return stream;
    }
}
