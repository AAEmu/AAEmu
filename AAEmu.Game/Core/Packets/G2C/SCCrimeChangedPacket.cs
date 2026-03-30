using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCrimeChangedPacket(int points, short crimePoints, int infamyPoints, short crimeState)
    : GamePacket(SCOffsets.SCCrimeChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(points);
        stream.Write(crimePoints);
        stream.Write(infamyPoints);
        stream.Write(crimeState); // No idea what exactly this is. Seems to be 1 if wanted, and 2 if captured
        return stream;
    }
}
