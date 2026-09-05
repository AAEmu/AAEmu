using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAuctionLimitedPricePacket(IReadOnlyList<AuctionLimitedPrice> caps)
    : GamePacket(SCOffsets.SCAuctionLimitedPricePacket, 1)
{
    public const int MaxEntries = 10;

    public override PacketStream Write(PacketStream stream)
    {
        var entries = caps ?? [];
        var count = Math.Min(entries.Count, MaxEntries);
        stream.Write(count);
        for (var i = 0; i < count; i++)
            stream.Write(entries[i]);
        return stream;
    }
}

public class AuctionLimitedPrice : PacketMarshaler
{
    public uint Type { get; set; }
    public long First { get; set; }
    public long Second { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Type);
        stream.Write(First);
        stream.Write(Second);
        return stream;
    }
}
