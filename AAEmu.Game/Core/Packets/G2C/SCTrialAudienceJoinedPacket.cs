using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces that someone joined a trial as an audience member.
/// </summary>
/// <remarks>
/// Body: trialId (u64), the audience member's object id (3-byte bc), audienceName (string).
/// The id was one byte wide before; the client's own serializer writes three, so the name string
/// and every following packet were shifted by two bytes.
/// </remarks>
public class SCTrialAudienceJoinedPacket(ulong trialId, uint audienceObjId, string audienceName)
    : GamePacket(SCOffsets.SCTrialAudienceJoinedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(trialId);
        stream.WriteBc(audienceObjId);
        stream.Write(audienceName);
        return stream;
    }
}
