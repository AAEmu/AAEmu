using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCRaceCongestionPacket() : GamePacket(SCOffsets.SCRaceCongestionPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        // 10.0.2.13: con[10] (u8) + forbidCharCreating (bool).
        for (var i = 0; i < 10; i++)
            stream.Write((byte)0); // con — 0 = LOW (race selectable)
        stream.Write(false);       // forbidCharCreating
        return stream;
    }
}
