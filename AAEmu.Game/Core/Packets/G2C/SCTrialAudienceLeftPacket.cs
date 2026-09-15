using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces that someone left a trial audience. Carries no trial id - the client's reader has only
/// the member's object id (3-byte bc) and their name.
/// </summary>
public class SCTrialAudienceLeftPacket(uint audienceObjId, string audiencename)
    : GamePacket(SCOffsets.SCTrialAudienceLeftPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(audienceObjId);
        stream.Write(audiencename);
        return stream;
    }
}
