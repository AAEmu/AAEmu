using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// One quote on the remodel tax reply. The client names the fields <c>bt</c>, <c>vt</c>, <c>pd</c>,
/// <c>wp</c>, <c>dtr</c> and stores each row as 24 bytes after reading them in that order.
/// </summary>
public readonly record struct RebuildHouseTaxQuote(uint Bt, byte Vt, ulong Pd, uint Wp, uint Dtr);

/// <summary>
/// The remodel window's reply. The window leaves its wait page only when this packet arrives for a
/// house the client already has; the house-tax and construction-quote packets fire different events
/// and must not be used here.
/// </summary>
/// <remarks>
/// Header is the house's timeline id, a dominion rate, then a count of quote rows (capped at 100).
/// The request that opens the window sends timeline id 0xFFFF ("the house already on screen"); the
/// reply names the resolved house so the client can find it in its own map.
/// </remarks>
public class SCRebuildHouseTaxInfoPacket(ushort tl, uint dominionRate, IReadOnlyList<RebuildHouseTaxQuote> quotes)
    : GamePacket(SCOffsets.SCRebuildHouseTaxInfoPacket, 1)
{
    public const int MaxQuotes = 100;

    public override PacketStream Write(PacketStream stream)
    {
        var rows = quotes ?? [];
        var count = rows.Count > MaxQuotes ? MaxQuotes : rows.Count;

        stream.Write(tl);
        stream.Write(dominionRate);
        stream.Write((uint)count);
        for (var i = 0; i < count; i++)
        {
            var row = rows[i];
            stream.Write(row.Bt);
            stream.Write(row.Vt);
            stream.Write(row.Pd);
            stream.Write(row.Wp);
            stream.Write(row.Dtr);
        }

        return stream;
    }
}
