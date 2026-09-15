using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Crime sheet of one character, opened from an evidence doodad or the trial window.
/// </summary>
/// <remarks>
/// Verified body layout: type (u64), defendantName (string), CharRace (u8), type (u32),
/// trialId (u64), sentence (u32), evidence object id (3-byte bc).
/// </remarks>
public class SCCrimeDataPacket(ulong type, string defendantName, byte charRace, uint type2, ulong trialId, uint sentence, uint evidenceObjId)
    : GamePacket(SCOffsets.SCCrimeDataPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(type);
        stream.Write(defendantName);
        stream.Write(charRace);
        stream.Write(type2);
        stream.Write(trialId);
        stream.Write(sentence);
        stream.WriteBc(evidenceObjId);
        return stream;
    }
}
