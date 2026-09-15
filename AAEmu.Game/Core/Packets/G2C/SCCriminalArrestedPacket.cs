using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces an arrest: who was arrested and who made the arrest. The client shows it as the
/// "criminal arrested" notice over the arrest itself.
/// </summary>
/// <remarks>
/// Body: forceImprison (u8), the criminal's object id (3-byte bc), criminalName, arresterName.
/// </remarks>
public class SCCriminalArrestedPacket(bool forceImprison, uint bc, string criminalName, string arresterName)
    : GamePacket(SCOffsets.SCCriminalArrestedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(forceImprison);
        stream.WriteBc(bc);
        stream.Write(criminalName);
        stream.Write(arresterName);
        return stream;
    }
}
