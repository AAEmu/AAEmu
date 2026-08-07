using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSExchangeRatioPacket(int exchangeRatio, DateTime timeStamp) : GamePacket(SCOffsets.SCICSExchangeRatioPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(exchangeRatio); // u32
        stream.Write(timeStamp);     // i64 (8-byte unix timestamp) — required
        return stream;
    }
}
