using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCRaceCongestionPacket() : GamePacket(SCOffsets.SCRaceCongestionPacket, 5)
{
    private const int NativeRaceCount = 10;
    private const byte LowCongestion = 0;

    public override PacketStream Write(PacketStream stream)
    {
        for (var i = 0; i < NativeRaceCount; i++)
            stream.Write(LowCongestion);
        stream.Write(false);
        return stream;
    }
}
