using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCJuryBeSeatedPacket(bool isWest, uint trial, int court, int juryNumber)
    : GamePacket(SCOffsets.SCJuryBeSeatedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isWest);
        stream.Write(trial);
        stream.Write(court);
        stream.Write(juryNumber);
        return stream;
    }
}
