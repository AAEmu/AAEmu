using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCChangeTrialStatePacket(uint trialId, byte state, int curJury, uint remainTime)
    : GamePacket(SCOffsets.SCChangeTrialStatePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(trialId);
        stream.Write(state);
        stream.Write(curJury);
        stream.Write(remainTime);
        return stream;

    }
}
