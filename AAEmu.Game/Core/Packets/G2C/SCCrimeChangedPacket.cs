using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCrimeChangedPacket(int points, short crimePoints, int infamyPoints, short crimeScore)
    : GamePacket(SCOffsets.SCCrimeChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(points);
        stream.Write(crimePoints);
        stream.Write(infamyPoints);
        stream.Write(crimeScore); // No idea what exactly this is
        return stream;
    }
}
