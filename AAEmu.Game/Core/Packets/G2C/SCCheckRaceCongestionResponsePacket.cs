using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// 10.0.2.13 response to CS_PACKET_CHECK_RACE_CONGESTION: 10 per-race congestion bytes (0 = LOW = open)
// followed by a "result" byte. RUNTIME-VERIFIED: result = 1 (canEnter) lets the client proceed into the world;
// result = 0 shows the "cannot enter the world with this character" congestion dialog. So pass canEnter=true.
public class SCCheckRaceCongestionResponsePacket(bool canEnter)
    : GamePacket(SCOffsets.SCCheckRaceCongestionResponsePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        for (var i = 0; i < 10; i++)
            stream.Write((byte)0); // con — per-race congestion, 0 = LOW (race open)
        stream.Write(canEnter);    // "result": 1 -> client proceeds; 0 -> congestion dialog
        return stream;
    }
}
