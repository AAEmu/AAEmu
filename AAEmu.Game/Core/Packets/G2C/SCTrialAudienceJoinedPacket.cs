using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTrialAudienceJoinedPacket(uint trialId, byte bc, string audienceName)
    : GamePacket(SCOffsets.SCTrialAudienceJoinedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(trialId);
        stream.Write(bc);
        stream.Write(audienceName);
        return stream;
    }
}
