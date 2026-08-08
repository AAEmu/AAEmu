using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Publishes the Marketplace exchange ratio and refresh timestamp.</summary>
public class SCICSExchangeRatioPacket(int exchangeRatio) : GamePacket(SCOffsets.SCICSExchangeRatioPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(exchangeRatio); // u32
        stream.Write(DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // i64 unix
        return stream;
    }
}
