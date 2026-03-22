using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCrimeChangedPacket(uint playerId, short crimePoints, int infamyPoints)
    : GamePacket(SCOffsets.SCCrimeChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(playerId); // Not sure if this is correct, but value doesn't seem to be used by the client
        stream.Write(crimePoints);
        stream.Write(infamyPoints);
        stream.Write((short)0); // No idea what this is, possibly jury related?
        return stream;
    }
}
