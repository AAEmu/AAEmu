using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTrialCanceledPacket(uint trialId) : GamePacket(SCOffsets.SCTrialCanceledPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(trialId);
        return stream;
    }
}
