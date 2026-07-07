using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSummonJuryPacket(uint trial, uint court, int juryNumber) : GamePacket(SCOffsets.SCSummonJuryPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(trial);
        stream.Write(court);
        stream.Write(juryNumber);
        return stream;
    }
}
