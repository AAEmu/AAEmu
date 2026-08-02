using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer:
/// i32 point, i16 crimePoint, i32 crimeRecord, i16 crimeScore, bool isLockupAndImprison.
/// The 1.2 layout stopped at crimeScore, so the client read the following packet's first byte as the
/// jail flag and everything after it shifted.
/// </remarks>
public class SCCrimeChangedPacket(int point, short crimePoint, int crimeRecord, short crimeScore, bool isLockupAndImprison = false)
    : GamePacket(SCOffsets.SCCrimeChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(point);
        stream.Write(crimePoint);
        stream.Write(crimeRecord);
        stream.Write(crimeScore);
        stream.Write(isLockupAndImprison);
        return stream;
    }
}
