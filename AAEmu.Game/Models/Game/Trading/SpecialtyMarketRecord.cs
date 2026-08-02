using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Trading;

/// <summary>
/// A historical specialty ratio and its Unix timestamp. XlGetCurrentFileTime and
/// XlFormatFileTime consume seconds in this client build, despite the legacy function name.
/// </summary>
public sealed class SpecialtyMarketRecord(int ratio, long recorded) : PacketMarshaler
{
    public int Ratio { get; } = ratio;
    public long Recorded { get; } = recorded;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Ratio);
        stream.Write(Recorded);
        return stream;
    }
}
